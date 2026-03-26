using IBApi;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;
using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class CandidateFinder(
        IStockUniverseProvider stockUniverseProvider,
        IStockPreFilter preFilter,
        IContractResolver contractResolver,
        IHistoricalDataService historicalData,
        ICandidateSignalAnalyzer signalAnalyzer,
        IWishListFilter wishListFilter,
        IWishListScore wishListScore,
        ICandidateFilter candidateFilter,
        ICandidateScore candidateScore,
        ITradeBuilder tradeBuilder,
        IScanCodeInfoService scannerPresets,
        IWishListReader wishListReader,
        IWishListMerger wishListMerger,
        IAgentPathService pathService,
        IMarketSettingsProvider marketSettingsProvider,
        IGetCandidatesSettingsProvider getCandidatesSettingsProvider,
        ITextLogger logger) : ICandidateFinder
    {
        private readonly IStockUniverseProvider _stockUniverseProvider = stockUniverseProvider;
        private readonly IStockPreFilter _preFilter = preFilter;
        private readonly IContractResolver _contractResolver = contractResolver;
        private readonly IHistoricalDataService _historicalData = historicalData;
        private readonly ICandidateSignalAnalyzer _signalAnalyzer = signalAnalyzer;
        private readonly IWishListFilter _wishListFilter = wishListFilter;
        private readonly IWishListScore _wishListScore = wishListScore;
        private readonly ICandidateFilter _candidateFilter = candidateFilter;
        private readonly ICandidateScore _candidateScore = candidateScore;
        private readonly ITradeBuilder _tradeBuilder = tradeBuilder;
        private readonly IScanCodeInfoService _scannerPresets = scannerPresets;
        private readonly IWishListReader _wishListReader = wishListReader;
        private readonly IWishListMerger _wishListMerger = wishListMerger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;
        private readonly IGetCandidatesSettingsProvider _getCandidatesSettingsProvider = getCandidatesSettingsProvider;
        private readonly ITextLogger _logger = logger;

        public async Task<CandidateSearchResult> FindAsync()
        {
            var marketTimezone = _marketSettingsProvider.Get().Timezone;
            var marketNow = GetMarketNow(marketTimezone);
            var todayMarketDate = marketNow.Date;

            var wishListPath = _pathService.GetWishListFile();
            var currentWishList = await _wishListReader.ReadAsync(wishListPath);

            var scannedWishListContexts = new Dictionary<string, WishListContext>(StringComparer.OrdinalIgnoreCase);
            var candidateResults = new Dictionary<string, CandidateDetails>(StringComparer.OrdinalIgnoreCase);

            foreach (var preset in _scannerPresets.GetAll())
            {
                var stocks = await _stockUniverseProvider.GetStocksAsync(preset.ScanCode);

                foreach (var stock in stocks)
                {
                    if (!_preFilter.Pass(stock))
                        continue;

                    Contract contract;

                    try
                    {
                        contract = await _contractResolver.ResolveStockAsync(stock.Ticker);
                    }
                    catch (Exception ex)
                    {
                        _logger.Info($"Skipping {stock.Ticker}: failed to resolve contract. {ex.Message}");
                        continue;
                    }

                    List<Candle>? candles;

                    try
                    {
                        var end = DateTime.UtcNow;
                        var start = end.AddDays(-60);

                        candles = await _historicalData.GetCandlesRange(
                            stock.Ticker,
                            contract,
                            Timeframe.H4,
                            start,
                            end);
                    }
                    catch (Exception ex)
                    {
                        _logger.Info($"Skipping {stock.Ticker}: failed to load candles. {ex.Message}");
                        continue;
                    }

                    if (candles == null || candles.Count < 80)
                    {
                        _logger.Info($"Skipping {stock.Ticker}: not enough candles ({candles?.Count ?? 0}).");
                        continue;
                    }

                    CandidateSignalSnapshot snapshot;

                    try
                    {
                        snapshot = _signalAnalyzer.Analyze(candles);
                    }
                    catch (Exception ex)
                    {
                        _logger.Info($"Skipping {stock.Ticker}: failed to analyze signals. {ex.Message}");
                        continue;
                    }

                    var lastPrice = candles[^1].Close;
                    var avgDollarVolumeDaily20 = CalculateAverageDollarVolumeDaily20(candles);

                    _logger.Info(
                        $"Processing ticker ({stock.Ticker}), " +
                        $"preset ({preset.ScanCode}), " +
                        $"stock type ({stock.StockType}), " +
                        $"trading class ({stock.TradingClass}), " +
                        $"exchange ({stock.Exchange}), " +
                        $"rank ({stock.Rank})");

                    if (!_wishListFilter.Pass(snapshot, lastPrice, avgDollarVolumeDaily20))
                    {
                        _logger.Info($"Wish list rejected: {stock.Ticker}");
                        continue;
                    }

                    var wishScore = _wishListScore.Calculate(snapshot);

                    var wishListItem = BuildWishListItem(
                        stock,
                        preset,
                        snapshot,
                        candles,
                        marketNow,
                        marketTimezone,
                        wishScore);

                    AddOrReplaceWishListContext(
                        scannedWishListContexts,
                        new WishListContext
                        {
                            Stock = stock,
                            Preset = preset,
                            Snapshot = snapshot,
                            Candles = candles,
                            ScanTimeMarket = marketNow,
                            AvgDollarVolumeDaily20 = avgDollarVolumeDaily20,
                            WishListItem = wishListItem
                        });
                }
            }

            var scannedWishListItems =
                scannedWishListContexts.Values
                    .Select(x => x.WishListItem)
                    .ToList();

            var mergedWishList = _wishListMerger.Merge(currentWishList, scannedWishListItems);

            var mergedMap = mergedWishList.ToDictionary(
                x => x.Ticker,
                x => x,
                StringComparer.OrdinalIgnoreCase);

            foreach (var ctx in scannedWishListContexts.Values)
            {
                if (!mergedMap.TryGetValue(ctx.Stock.Ticker, out var mergedWishItem))
                    continue;

                var firstSeenDate = mergedWishItem.FirstSeenMarketTime?.Date;

                if (firstSeenDate == null || firstSeenDate.Value >= todayMarketDate)
                {
                    _logger.Info($"Entry skipped for {ctx.Stock.Ticker}: first seen today in wish list.");
                    continue;
                }

                var trade = ctx.Trade ??= BuildTradePlan(ctx);

                if (!_candidateFilter.Pass(ctx.Snapshot, trade.EntryPrice, ctx.AvgDollarVolumeDaily20))
                {
                    _logger.Info($"Entry rejected after wish list pass: {ctx.Stock.Ticker}");
                    continue;
                }

                var entryScore = _candidateScore.Calculate(ctx.Snapshot);
                var dailyScore = mergedWishItem.Score.DailyScore ?? 0m;
                var weeklyScore = mergedWishItem.Score.WeeklyScore ?? 0m;
                var finalScore = dailyScore + weeklyScore + entryScore;

                var candidateItem = BuildCandidateItem(
                    ctx.Stock,
                    ctx.Preset,
                    ctx.Snapshot,
                    ctx.Candles,
                    trade,
                    ctx.ScanTimeMarket,
                    marketTimezone,
                    dailyScore,
                    weeklyScore,
                    entryScore,
                    finalScore);

                AddOrReplaceHigherScore(candidateResults, candidateItem, "candidates");
            }

            var promotedTickers = candidateResults.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var finalWishList = mergedWishList
                .Where(x => !promotedTickers.Contains(x.Ticker))
                .ToList();

            return new CandidateSearchResult
            {
                Candidates = [.. candidateResults.Values.OrderByDescending(x => x.Score.Score)],
                WishList = finalWishList
            };
        }

        private void AddOrReplaceWishListContext(
            Dictionary<string, WishListContext> results,
            WishListContext item)
        {
            if (results.TryGetValue(item.Stock.Ticker, out var existing))
            {
                if (item.WishListItem.Score.Score > existing.WishListItem.Score.Score)
                {
                    results[item.Stock.Ticker] = item;

                    _logger.Info(
                        $"Ticker {item.Stock.Ticker} replaced existing wish list item with higher score. " +
                        $"Old preset: {existing.Preset.ScanCode}, new preset: {item.Preset.ScanCode}");
                }
                else
                {
                    _logger.Info(
                        $"Ticker {item.Stock.Ticker} already exists in wish list. " +
                        $"Keeping existing item from preset {existing.Preset.ScanCode}");
                }
            }
            else
            {
                results[item.Stock.Ticker] = item;
                _logger.Info($"Ticker {item.Stock.Ticker} added to wish list. Preset: {item.Preset.ScanCode}");
            }
        }

        private void AddOrReplaceHigherScore(
            Dictionary<string, CandidateDetails> results,
            CandidateDetails item,
            string bucketName)
        {
            if (results.TryGetValue(item.Ticker, out var existing))
            {
                if (item.Score.Score > existing.Score.Score)
                {
                    results[item.Ticker] = item;

                    _logger.Info(
                        $"Ticker {item.Ticker} replaced existing {bucketName} item with higher score. " +
                        $"Old preset: {existing.Scan.PresetScanCode}, new preset: {item.Scan.PresetScanCode}");
                }
                else
                {
                    _logger.Info(
                        $"Ticker {item.Ticker} already exists in {bucketName}. " +
                        $"Keeping existing item from preset {existing.Scan.PresetScanCode}");
                }
            }
            else
            {
                results[item.Ticker] = item;
                _logger.Info($"Ticker {item.Ticker} added to {bucketName}. Preset: {item.Scan.PresetScanCode}");
            }
        }

        private TradePlanInfo BuildTradePlan(WishListContext ctx)
        {
            var trade = _tradeBuilder.Build(ctx.Candles);

            return new TradePlanInfo
            {
                EntryPrice = trade.Entry,
                ExitPrice = trade.Exit,
                StopLoss = trade.Stop,
                ProfitPercent = CalculatePercent(trade.Entry, trade.Exit),
                LossPercent = CalculatePercent(trade.Entry, trade.Stop)
            };
        }

        private static WishListItem BuildWishListItem(
            StockInfo stock,
            PresetScanCode preset,
            CandidateSignalSnapshot snapshot,
            List<Candle> candles,
            DateTime scanTimeMarket,
            string scanTimeZone,
            WishListScoreResult wishScore)
        {
            var targetForecast = CalculateWishListTargetForecast(snapshot, candles, scanTimeMarket);

            return new WishListItem
            {
                Ticker = stock.Ticker,
                Scan = new ScanInfo
                {
                    PresetScanCode = preset.ScanCode,
                    PresetDescription = preset.Description,
                    ScanTimeMarket = scanTimeMarket,
                    ScanTimeZone = scanTimeZone
                },
                Score = new ScoreInfo
                {
                    Score = wishScore.TotalScore,
                    WeeklyScore = wishScore.WeeklyScore,
                    DailyScore = wishScore.DailyScore,
                    EntryScore = null
                },
                Context = new MarketContextInfo
                {
                    DistanceTo20dHigh = snapshot.Current.DistanceTo20dHigh,
                    DistanceTo52wHigh = snapshot.Current.DistanceTo52wHigh,
                    DailyRSI14 = snapshot.Current.DailyRSI14,
                    Notes = BuildWishListNotes(snapshot)
                },
                FirstSeenMarketTime = scanTimeMarket,
                LastEvaluatedMarketTime = scanTimeMarket,
                ExpectedTargetMarketTime = targetForecast.ExpectedTargetMarketTime,
                ExpectedBarsToTarget = targetForecast.ExpectedBarsToTarget
            };
        }

        private static CandidateDetails BuildCandidateItem(
            StockInfo stock,
            PresetScanCode preset,
            CandidateSignalSnapshot snapshot,
            List<Candle> candles,
            TradePlanInfo trade,
            DateTime scanTimeMarket,
            string scanTimeZone,
            decimal dailyScore,
            decimal weeklyScore,
            decimal entryScore,
            decimal finalScore)
        {
            return new CandidateDetails
            {
                Ticker = stock.Ticker,
                Scan = new ScanInfo
                {
                    PresetScanCode = preset.ScanCode,
                    PresetDescription = preset.Description,
                    ScanTimeMarket = scanTimeMarket,
                    ScanTimeZone = scanTimeZone
                },
                Score = new ScoreInfo
                {
                    Score = finalScore,
                    WeeklyScore = weeklyScore,
                    DailyScore = dailyScore,
                    EntryScore = entryScore
                },
                Context = new MarketContextInfo
                {
                    DistanceTo20dHigh = snapshot.Current.DistanceTo20dHigh,
                    DistanceTo52wHigh = snapshot.Current.DistanceTo52wHigh,
                    DailyRSI14 = snapshot.Current.DailyRSI14,
                    Notes = BuildCandidateNotes(snapshot)
                },
                TradePlan = trade,
                Diagnostics = BuildDiagnostics(snapshot, candles)
            };
        }

        private static CandidateDiagnostics BuildDiagnostics(
            CandidateSignalSnapshot snapshot,
            List<Candle> candles)
        {
            return new CandidateDiagnostics
            {
                Pullback10d = CalculatePullbackByCalendarDays(candles, 10),
                VolumeRatio20 = CalculateVolumeRatio20(candles),
                ATRRatio = CalculateAtrRatio(candles, 14),
                TrendPosition = snapshot.Current.H4MaSignedDistancePct,
                DailyTrendPosition = snapshot.Current.DailyMaSignedDistancePct,
                DailyPullback10d = CalculateDailyPullback10d(candles),
                BBMidSignedDistancePct = CalculateSmaSignedDistancePct(candles, 20),
                WeeklyMACDHistDelta = CalculateWeeklyMacdHistDelta(candles)
            };
        }

        private static (int? ExpectedBarsToTarget, DateTime? ExpectedTargetMarketTime) CalculateWishListTargetForecast(
            CandidateSignalSnapshot snapshot,
            List<Candle> candles,
            DateTime scanTimeMarket)
        {
            var currentDistancePct = snapshot.Current.DailyMaSignedDistancePct;

            if (currentDistancePct >= 0m)
                return (0, scanTimeMarket);

            var progressPerBar = snapshot.DailyMaDelta3 / 3m;

            if (progressPerBar <= 0m && snapshot.H4MaDelta3 > 0m)
            {
                progressPerBar = snapshot.H4MaDelta3 / 3m;
            }

            if (progressPerBar <= 0m)
                return (null, null);

            var remainingDistancePct = Math.Abs(currentDistancePct);
            var expectedBars = (int)Math.Ceiling((double)(remainingDistancePct / progressPerBar));

            if (expectedBars <= 0)
                return (0, scanTimeMarket);

            var cappedBars = Math.Min(expectedBars, 60);
            var step = EstimateMarketBarStep(candles);

            return (cappedBars, scanTimeMarket.Add(step * cappedBars));
        }

        private static TimeSpan EstimateMarketBarStep(List<Candle> candles)
        {
            if (candles.Count < 2)
                return TimeSpan.FromHours(4);

            var steps = new List<TimeSpan>();

            for (var i = Math.Max(1, candles.Count - 10); i < candles.Count; i++)
            {
                var step = candles[i].Time - candles[i - 1].Time;

                if (step > TimeSpan.Zero)
                    steps.Add(step);
            }

            if (steps.Count == 0)
                return TimeSpan.FromHours(4);

            var ordered = steps.OrderBy(x => x).ToList();
            return ordered[ordered.Count / 2];
        }

        private static decimal CalculatePullbackByCalendarDays(List<Candle> candles, int days)
        {
            if (candles.Count == 0)
                return 0m;

            var lastTime = candles[^1].Time;
            var fromTime = lastTime.AddDays(-days);

            var window = candles
                .Where(x => x.Time >= fromTime)
                .ToList();

            if (window.Count == 0)
                return 0m;

            var highest = window.Max(x => x.High);
            var lastClose = candles[^1].Close;

            if (highest == 0m)
                return 0m;

            return (lastClose - highest) / highest * 100m;
        }

        private static decimal CalculateVolumeRatio20(List<Candle> candles)
        {
            if (candles.Count < 21)
                return 0m;

            var lastVolume = candles[^1].Volume;
            var avgVolume = candles.Skip(Math.Max(0, candles.Count - 21)).Take(20).Average(x => x.Volume);

            if (avgVolume == 0m)
                return 0m;

            return lastVolume / avgVolume;
        }

        private static decimal CalculateAtrRatio(List<Candle> candles, int period)
        {
            if (candles.Count < period + 1)
                return 0m;

            var ranges = new List<decimal>();

            for (var i = candles.Count - period; i < candles.Count; i++)
            {
                var current = candles[i];
                var previousClose = candles[i - 1].Close;

                var tr = Math.Max(
                    current.High - current.Low,
                    Math.Max(
                        Math.Abs(current.High - previousClose),
                        Math.Abs(current.Low - previousClose)));

                ranges.Add(tr);
            }

            var atr = ranges.Average();
            var lastClose = candles[^1].Close;

            if (lastClose == 0m)
                return 0m;

            return atr / lastClose;
        }

        private static decimal CalculateDailyPullback10d(List<Candle> candles)
        {
            var daily = candles
                .GroupBy(x => x.Time.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    High = g.Max(x => x.High),
                    Close = g.OrderBy(x => x.Time).Last().Close
                })
                .OrderBy(x => x.Date)
                .ToList();

            if (daily.Count == 0)
                return 0m;

            var window = daily.TakeLast(10).ToList();
            var highest = window.Max(x => x.High);
            var lastClose = daily[^1].Close;

            if (highest == 0m)
                return 0m;

            return (lastClose - highest) / highest * 100m;
        }

        private static decimal CalculateSmaSignedDistancePct(List<Candle> candles, int period)
        {
            if (candles.Count < period)
                return 0m;

            var sma = candles.TakeLast(period).Average(x => x.Close);
            var lastClose = candles[^1].Close;

            if (sma == 0m)
                return 0m;

            return (lastClose - sma) / sma * 100m;
        }

        private static decimal CalculateWeeklyMacdHistDelta(List<Candle> candles)
        {
            var weekly = candles
                .GroupBy(x => GetWeekStart(x.Time.Date))
                .Select(g => new Candle
                {
                    Time = g.Max(x => x.Time),
                    Open = g.OrderBy(x => x.Time).First().Open,
                    High = g.Max(x => x.High),
                    Low = g.Min(x => x.Low),
                    Close = g.OrderBy(x => x.Time).Last().Close,
                    Volume = g.Sum(x => x.Volume),
                    Timeframe = Timeframe.W1
                })
                .OrderBy(x => x.Time)
                .ToList();

            if (weekly.Count < 10)
                return 0m;

            var macd = CalculateMacdHistogram(weekly);

            if (macd.Count < 2)
                return 0m;

            return macd[^1] - macd[^2];
        }

        private static List<decimal> CalculateMacdHistogram(List<Candle> candles)
        {
            var closes = candles.Select(x => x.Close).ToList();

            var ema12 = CalculateEmaSeries(closes, 12);
            var ema26 = CalculateEmaSeries(closes, 26);

            var macdLine = ema12.Zip(ema26, (a, b) => a - b).ToList();
            var signal = CalculateEmaSeries(macdLine, 9);

            return macdLine.Zip(signal, (m, s) => m - s).ToList();
        }

        private static List<decimal> CalculateEmaSeries(List<decimal> values, int period)
        {
            var result = new List<decimal>();

            if (values.Count == 0)
                return result;

            var multiplier = 2m / (period + 1);
            var ema = values[0];

            result.Add(ema);

            for (var i = 1; i < values.Count; i++)
            {
                ema = ((values[i] - ema) * multiplier) + ema;
                result.Add(ema);
            }

            return result;
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
            return date.AddDays(-diff).Date;
        }

        private static decimal CalculateAverageDollarVolumeDaily20(List<Candle> candles)
        {
            var dailyDollarVolumes = candles
                .GroupBy(x => x.Time.Date)
                .Select(g =>
                {
                    var dayClose = g.OrderBy(x => x.Time).Last().Close;
                    var dayVolume = g.Sum(x => x.Volume);
                    return dayClose * dayVolume;
                })
                .TakeLast(20)
                .ToList();

            if (dailyDollarVolumes.Count == 0)
                return 0m;

            return dailyDollarVolumes.Average();
        }

        private static decimal CalculatePercent(decimal from, decimal to)
        {
            if (from == 0m)
                return 0m;

            return (to - from) / from * 100m;
        }

        private static string BuildWishListNotes(CandidateSignalSnapshot snapshot)
        {
            var parts = new List<string>();

            if (snapshot.Current.DailyMaSignedDistancePct < 0m)
                parts.Add("below daily MA");

            if (snapshot.Current.DailyMACDLineMinusSignal <= 0m)
                parts.Add("daily MACD weak/negative");

            if (snapshot.Current.DailyRSI14 < 50m)
                parts.Add("daily RSI below neutral");

            if (snapshot.DailyMaDelta3 > 0m || snapshot.DailyRsiDelta3 > 0m || snapshot.DailyMacdDelta3 > 0m)
                parts.Add("early daily improvement");

            return parts.Count == 0
                ? "pullback context"
                : string.Join(", ", parts);
        }

        private static string BuildCandidateNotes(CandidateSignalSnapshot snapshot)
        {
            var parts = new List<string>();

            if (snapshot.DailyMaDelta3 > 0m)
                parts.Add("daily MA improving");

            if (snapshot.DailyRsiDelta3 > 0m)
                parts.Add("daily RSI improving");

            if (snapshot.DailyMacdDelta3 > 0m)
                parts.Add("daily MACD improving");

            if (snapshot.H4MaDelta3 > 0m)
                parts.Add("H4 MA improving");

            if (snapshot.H4RsiDelta3 > 0m)
                parts.Add("H4 RSI improving");

            if (snapshot.H4MacdDelta3 > 0m)
                parts.Add("H4 MACD improving");

            return parts.Count == 0
                ? "entry confirmed"
                : string.Join(", ", parts);
        }

        private static DateTime GetMarketNow(string timezoneId)
        {
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);
        }

        private sealed class WishListContext
        {
            public required StockInfo Stock { get; init; }
            public required PresetScanCode Preset { get; init; }
            public required CandidateSignalSnapshot Snapshot { get; init; }
            public required List<Candle> Candles { get; init; }
            public required DateTime ScanTimeMarket { get; init; }
            public required decimal AvgDollarVolumeDaily20 { get; init; }
            public required WishListItem WishListItem { get; init; }
            public TradePlanInfo? Trade { get; set; }
        }
    }
}