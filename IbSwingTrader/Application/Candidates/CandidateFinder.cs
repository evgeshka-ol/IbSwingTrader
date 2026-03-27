using IBApi;

namespace IbSwingTrader.Application.Candidates
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
            var getCandidatesSettings = _getCandidatesSettingsProvider.Get();
            var finderSettings = getCandidatesSettings.Finder;

            var marketTimezone = _marketSettingsProvider.Get().Timezone;
            var marketNow = GetMarketNow(marketTimezone);
            var todayMarketDate = marketNow.Date;

            _logger.Info(
                $"CandidateFinder settings: " +
                $"LookbackCalendarDays={finderSettings.LookbackCalendarDays}, " +
                $"MinimumCandles={finderSettings.MinimumCandles}, " +
                $"AvgVolumePeriod={finderSettings.AvgVolumePeriod}, " +
                $"CandleCount={finderSettings.CandleCount}");

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
                        var start = end.AddDays(-finderSettings.LookbackCalendarDays);

                        candles = await _historicalData.GetCandlesRange(
                            stock.Ticker,
                            contract,
                            Timeframe.H4,
                            start,
                            end);

                        if (candles != null &&
                            finderSettings.CandleCount > 0 &&
                            candles.Count > finderSettings.CandleCount)
                        {
                            var trimmedCandles = candles
                                .TakeLast(finderSettings.CandleCount)
                                .ToList();

                            var trimmedWeeklyBars = BuildWeeklyBars(trimmedCandles);

                            if (trimmedWeeklyBars.Count >= 20)
                            {
                                candles = trimmedCandles;

                                _logger.Info(
                                    $"Ticker history trimmed: {stock.Ticker}. " +
                                    $"H4={candles.Count}, W1={trimmedWeeklyBars.Count}");
                            }
                            else
                            {
                                _logger.Info(
                                    $"Ticker history trim skipped: {stock.Ticker}. " +
                                    $"RequestedH4={finderSettings.CandleCount}, " +
                                    $"TrimmedW1={trimmedWeeklyBars.Count} is too short for weekly analysis.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Info($"Skipping {stock.Ticker}: failed to load candles. {ex.Message}");
                        continue;
                    }

                    if (candles == null || candles.Count < finderSettings.MinimumCandles)
                    {
                        _logger.Info(
                            $"Skipping {stock.Ticker}: not enough candles " +
                            $"({candles?.Count ?? 0} < {finderSettings.MinimumCandles}).");
                        continue;
                    }

                    var dailyBars = BuildDailyBars(candles);
                    var weeklyBars = BuildWeeklyBars(candles);

                    _logger.Info(
                        $"Ticker history prepared: {stock.Ticker}. " +
                        $"H4={candles.Count}, D1={dailyBars.Count}, W1={weeklyBars.Count}");

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
                    var avgDollarVolume = CalculateAverageDollarVolumeDaily(candles, finderSettings.AvgVolumePeriod);

                    _logger.Info(
                        $"Processing ticker ({stock.Ticker}), " +
                        $"preset ({preset.ScanCode}), " +
                        $"stock type ({stock.StockType}), " +
                        $"trading class ({stock.TradingClass}), " +
                        $"exchange ({stock.Exchange}), " +
                        $"rank ({stock.Rank}), " +
                        $"avgDollarVolume={avgDollarVolume}");

                    if (!_wishListFilter.Pass(snapshot, lastPrice, avgDollarVolume))
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
                            AvgDollarVolumeDaily = avgDollarVolume,
                            WishListItem = wishListItem
                        });
                }
            }

            var scannedWishListItems = scannedWishListContexts.Values
                .Select(x => x.WishListItem)
                .ToList();

            var mergedWishList = _wishListMerger.Merge(currentWishList, scannedWishListItems);
            var forecastedCount = mergedWishList.Count(x => x.ExpectedBarsToTarget != null);

            _logger.Info($"WishList merged. Total={mergedWishList.Count}, WithForecast={forecastedCount}");

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

                if (!_candidateFilter.Pass(ctx.Snapshot, trade.EntryPrice, ctx.AvgDollarVolumeDaily))
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

            var finalForecastedCount = finalWishList.Count(x => x.ExpectedBarsToTarget != null);
            _logger.Info($"WishList final. Total={finalWishList.Count}, WithForecast={finalForecastedCount}");

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

        private WishListItem BuildWishListItem(
            StockInfo stock,
            PresetScanCode preset,
            CandidateSignalSnapshot snapshot,
            List<Candle> candles,
            DateTime scanTimeMarket,
            string scanTimeZone,
            WishListScoreResult wishScore)
        {
            _logger.Info(
                $"WishList item build started: {stock.Ticker}. " +
                $"DailyMaSignedDistancePct={snapshot.Current.DailyMaSignedDistancePct}, " +
                $"WeeklyMaSignedDistancePct={snapshot.Current.WeeklyMaSignedDistancePct}, " +
                $"DailyMaDelta3={snapshot.DailyMaDelta3}, " +
                $"H4MaDelta3={snapshot.H4MaDelta3}, " +
                $"DailyRsiDelta3={snapshot.DailyRsiDelta3}, " +
                $"DailyMacdDelta3={snapshot.DailyMacdDelta3}");

            var targetForecast = CalculateWishListTargetForecast(
                stock.Ticker,
                snapshot,
                candles,
                scanTimeMarket);

            _logger.Info(
                $"WishList item build completed: {stock.Ticker}. " +
                $"ExpectedBarsToTarget={targetForecast.ExpectedBarsToTarget?.ToString() ?? "null"}, " +
                $"ExpectedTargetMarketTime={targetForecast.ExpectedTargetMarketTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null"}");

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

        private (int? ExpectedBarsToTarget, DateTime? ExpectedTargetMarketTime) CalculateWishListTargetForecast(
            string ticker,
            CandidateSignalSnapshot snapshot,
            List<Candle> candles,
            DateTime scanTimeMarket)
        {
            var currentDistancePct = snapshot.Current.DailyMaSignedDistancePct;

            _logger.Info(
                $"WishList forecast input: {ticker}. " +
                $"CurrentDailyDistancePct={currentDistancePct}, " +
                $"DailyMaDelta3={snapshot.DailyMaDelta3}, " +
                $"H4MaDelta3={snapshot.H4MaDelta3}, " +
                $"DailyRsiDelta3={snapshot.DailyRsiDelta3}, " +
                $"DailyMacdDelta3={snapshot.DailyMacdDelta3}");

            if (currentDistancePct >= 0m)
            {
                _logger.Info($"WishList forecast: {ticker} reached/exceeded daily mid already.");
                return (0, scanTimeMarket);
            }

            decimal progressPerBar = 0m;
            string progressSource = "none";

            if (snapshot.DailyMaDelta3 > 0m)
            {
                progressPerBar = snapshot.DailyMaDelta3 / 3m;
                progressSource = "daily-ma";
            }
            else if (snapshot.H4MaDelta3 > 0m)
            {
                progressPerBar = snapshot.H4MaDelta3 / 3m;
                progressSource = "h4-ma";
            }
            else if (snapshot.DailyRsiDelta3 > 0m || snapshot.DailyMacdDelta3 > 0m)
            {
                progressPerBar = Math.Abs(currentDistancePct) / 12m;
                progressSource = "daily-rsi-macd-fallback";
            }

            _logger.Info(
                $"WishList forecast progress: {ticker}. " +
                $"ProgressSource={progressSource}, " +
                $"ProgressPerBar={progressPerBar}");

            if (progressPerBar <= 0m)
            {
                _logger.Info($"WishList forecast failed: {ticker}. No positive progress signal.");
                return (null, null);
            }

            var remainingDistancePct = Math.Abs(currentDistancePct);
            var expectedBars = (int)Math.Ceiling((double)(remainingDistancePct / progressPerBar));

            _logger.Info(
                $"WishList forecast raw result: {ticker}. " +
                $"RemainingDistancePct={remainingDistancePct}, " +
                $"ExpectedBarsRaw={expectedBars}");

            if (expectedBars <= 0)
            {
                _logger.Info($"WishList forecast normalized to zero bars: {ticker}.");
                return (0, scanTimeMarket);
            }

            var cappedBars = Math.Min(expectedBars, 60);
            var step = EstimateMarketBarStep(candles);
            var expectedTargetMarketTime = scanTimeMarket.Add(step * cappedBars);

            _logger.Info(
                $"WishList forecast final result: {ticker}. " +
                $"ExpectedBarsCapped={cappedBars}, " +
                $"Step={step}, " +
                $"ExpectedTargetMarketTime={expectedTargetMarketTime:yyyy-MM-dd HH:mm:ss}");

            return (cappedBars, expectedTargetMarketTime);
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
            var start = lastTime.AddDays(-days);

            var range = candles
                .Where(x => x.Time >= start)
                .ToList();

            if (range.Count == 0)
                return 0m;

            var highest = range.Max(x => x.High);
            var close = candles[^1].Close;

            if (highest <= 0m)
                return 0m;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalculateVolumeRatio20(List<Candle> candles)
        {
            if (candles.Count < 21)
                return 0m;

            var currentVolume = candles[^1].Volume;

            var avgVolume20 = candles
                .Skip(candles.Count - 21)
                .Take(20)
                .Average(x => x.Volume);

            if (avgVolume20 <= 0m)
                return 0m;

            return currentVolume / avgVolume20;
        }

        private static decimal CalculateAtrRatio(List<Candle> candles, int length)
        {
            if (candles.Count < length + 1)
                return 0m;

            var trueRanges = new List<decimal>();

            for (var i = candles.Count - length; i < candles.Count; i++)
            {
                var current = candles[i];
                var prevClose = candles[i - 1].Close;

                var tr = Math.Max(
                    current.High - current.Low,
                    Math.Max(
                        Math.Abs(current.High - prevClose),
                        Math.Abs(current.Low - prevClose)));

                trueRanges.Add(tr);
            }

            if (trueRanges.Count == 0)
                return 0m;

            var atr = trueRanges.Average();
            var close = candles[^1].Close;

            if (close == 0m)
                return 0m;

            return atr / close * 100m;
        }

        private static decimal CalculateDailyPullback10d(List<Candle> candles)
        {
            var dailyBars = BuildDailyBars(candles);

            if (dailyBars.Count == 0)
                return 0m;

            var range = dailyBars.TakeLast(10).ToList();
            var highest = range.Max(x => x.High);
            var close = dailyBars[^1].Close;

            if (highest <= 0m)
                return 0m;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalculateSmaSignedDistancePct(List<Candle> candles, int length)
        {
            if (candles.Count < length)
                return 0m;

            var sma = candles.TakeLast(length).Average(x => x.Close);

            if (sma == 0m)
                return 0m;

            var close = candles[^1].Close;
            return (close - sma) / sma * 100m;
        }

        private static decimal? CalculateWeeklyMacdHistDelta(List<Candle> candles)
        {
            var weeklyCloses = BuildWeeklyBars(candles)
                .Select(x => x.Close)
                .ToList();

            var macdSeries = BuildMacdSeries(weeklyCloses);

            if (macdSeries.Count < 2)
                return null;

            var currentHist = macdSeries[^1].Macd - macdSeries[^1].Signal;
            var prevHist = macdSeries[^2].Macd - macdSeries[^2].Signal;

            return currentHist - prevHist;
        }

        private static List<Candle> BuildDailyBars(List<Candle> candles)
        {
            var result = new List<Candle>();

            foreach (var group in candles.GroupBy(x => x.Time.Date).OrderBy(x => x.Key))
            {
                var ordered = group.OrderBy(x => x.Time).ToList();

                result.Add(new Candle
                {
                    Timeframe = Timeframe.D1,
                    Time = group.Key,
                    Open = ordered[0].Open,
                    High = ordered.Max(x => x.High),
                    Low = ordered.Min(x => x.Low),
                    Close = ordered[^1].Close,
                    Volume = ordered.Sum(x => x.Volume)
                });
            }

            return result;
        }

        private static List<Candle> BuildWeeklyBars(List<Candle> candles)
        {
            var result = new List<Candle>();

            foreach (var group in candles
                .GroupBy(x => StartOfWeek(x.Time.Date))
                .OrderBy(x => x.Key))
            {
                var ordered = group.OrderBy(x => x.Time).ToList();

                result.Add(new Candle
                {
                    Timeframe = Timeframe.W1,
                    Time = group.Key,
                    Open = ordered[0].Open,
                    High = ordered.Max(x => x.High),
                    Low = ordered.Min(x => x.Low),
                    Close = ordered[^1].Close,
                    Volume = ordered.Sum(x => x.Volume)
                });
            }

            return result;
        }

        private static DateTime StartOfWeek(DateTime value)
        {
            var diff = (7 + (value.DayOfWeek - DayOfWeek.Monday)) % 7;
            return value.AddDays(-diff).Date;
        }

        private static List<MacdPoint> BuildMacdSeries(List<decimal> closes)
        {
            var result = new List<MacdPoint>();

            if (closes.Count == 0)
                return result;

            decimal? ema12 = null;
            decimal? ema26 = null;
            decimal? signal = null;

            const decimal k12 = 2m / 13m;
            const decimal k26 = 2m / 27m;
            const decimal k9 = 2m / 10m;

            foreach (var close in closes)
            {
                ema12 = ema12 == null ? close : ema12.Value + (close - ema12.Value) * k12;
                ema26 = ema26 == null ? close : ema26.Value + (close - ema26.Value) * k26;

                var macd = ema12.Value - ema26.Value;
                signal = signal == null ? macd : signal.Value + (macd - signal.Value) * k9;

                result.Add(new MacdPoint
                {
                    Macd = macd,
                    Signal = signal.Value
                });
            }

            return result;
        }

        private static decimal CalculateAverageDollarVolumeDaily(List<Candle> candles, int period)
        {
            var actualPeriod = Math.Max(1, period);

            var dailyDollarVolumes = candles
                .GroupBy(x => x.Time.Date)
                .Select(g =>
                {
                    var ordered = g.OrderBy(x => x.Time).ToList();
                    var dayClose = ordered[^1].Close;
                    var dayVolume = ordered.Sum(x => x.Volume);
                    return dayClose * dayVolume;
                })
                .TakeLast(actualPeriod)
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

        private sealed class MacdPoint
        {
            public decimal Macd { get; init; }
            public decimal Signal { get; init; }
        }

        private sealed class WishListContext
        {
            public required StockInfo Stock { get; init; }
            public required PresetScanCode Preset { get; init; }
            public required CandidateSignalSnapshot Snapshot { get; init; }
            public required List<Candle> Candles { get; init; }
            public required DateTime ScanTimeMarket { get; init; }
            public required decimal AvgDollarVolumeDaily { get; init; }
            public required WishListItem WishListItem { get; init; }
            public TradePlanInfo? Trade { get; set; }
        }
    }
}
