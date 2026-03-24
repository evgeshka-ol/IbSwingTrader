using IBApi;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;
using IbSwingTrader.Models.Stocks;

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
        private readonly ITextLogger _logger = logger;

        public async Task<CandidateSearchResult> FindAsync()
        {
            var wishListContexts = new Dictionary<string, WishListContext>(StringComparer.OrdinalIgnoreCase);
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
                    var trade = _tradeBuilder.Build(candles);
                    var scanTime = DateTime.UtcNow;

                    var wishListItem = BuildWishListItem(
                        stock,
                        preset,
                        snapshot,
                        trade,
                        scanTime,
                        wishScore);

                    AddOrReplaceWishListContext(
                        wishListContexts,
                        new WishListContext
                        {
                            Stock = stock,
                            Preset = preset,
                            Snapshot = snapshot,
                            Trade = trade,
                            ScanTime = scanTime,
                            AvgDollarVolumeDaily20 = avgDollarVolumeDaily20,
                            WishListItem = wishListItem
                        });
                }
            }

            foreach (var ctx in wishListContexts.Values)
            {
                var lastPrice = ctx.Trade.Entry;

                if (!_candidateFilter.Pass(ctx.Snapshot, lastPrice, ctx.AvgDollarVolumeDaily20))
                {
                    _logger.Info($"Entry rejected after wish list pass: {ctx.Stock.Ticker}");
                    continue;
                }

                var entryScore = _candidateScore.Calculate(ctx.Snapshot);
                var finalScore = ctx.WishListItem.Score + entryScore;

                var candidateItem = BuildCandidateItem(
                    ctx.Stock,
                    ctx.Preset,
                    ctx.Snapshot,
                    ctx.Trade,
                    ctx.ScanTime,
                    ctx.WishListItem.Score,
                    entryScore,
                    finalScore);

                AddOrReplaceHigherScore(
                    candidateResults,
                    candidateItem,
                    "candidate");
            }

            return new CandidateSearchResult
            {
                WishList = [.. wishListContexts.Values
                    .Select(x => x.WishListItem)
                    .OrderByDescending(x => x.Score)],

                Candidates = [.. candidateResults.Values
                    .OrderByDescending(x => x.Score)
                    .Take(10)]
            };
        }

        private void AddOrReplaceWishListContext(
            Dictionary<string, WishListContext> results,
            WishListContext item)
        {
            if (results.TryGetValue(item.Stock.Ticker, out var existing))
            {
                if (item.WishListItem.Score > existing.WishListItem.Score)
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
                if (item.Score > existing.Score)
                {
                    results[item.Ticker] = item;

                    _logger.Info(
                        $"Ticker {item.Ticker} replaced existing {bucketName} item with higher score. " +
                        $"Old preset: {existing.PresetScanCode}, new preset: {item.PresetScanCode}");
                }
                else
                {
                    _logger.Info(
                        $"Ticker {item.Ticker} already exists in {bucketName}. " +
                        $"Keeping existing item from preset {existing.PresetScanCode}");
                }
            }
            else
            {
                results[item.Ticker] = item;
                _logger.Info($"Ticker {item.Ticker} added to {bucketName}. Preset: {item.PresetScanCode}");
            }
        }

        private static CandidateDetails BuildWishListItem(
            StockInfo stock,
            PresetScanCode preset,
            CandidateSignalSnapshot snapshot,
            TradePlan trade,
            DateTime scanTime,
            decimal wishScore)
        {
            return new CandidateDetails
            {
                Ticker = stock.Ticker,
                PresetScanCode = preset.ScanCode,
                PresetDescription = preset.Description,

                EntryPrice = trade.Entry,
                ExitPrice = trade.Exit,
                StopLoss = trade.Stop,

                ProfitPercent = CalculatePercent(trade.Entry, trade.Exit),
                LossPercent = CalculatePercent(trade.Entry, trade.Stop),

                Score = wishScore,
                WeeklyScore = null,
                DailyScore = wishScore,
                EntryScore = null,

                IsWishList = true,

                DistanceTo20dHigh = snapshot.Current.DistanceTo20dHigh,
                DistanceTo52wHigh = snapshot.Current.DistanceTo52wHigh,
                DailyRSI14 = snapshot.Current.DailyRSI14,

                ScanTimeMarket = scanTime,
                Notes = BuildWishListNotes(snapshot)
            };
        }

        private static CandidateDetails BuildCandidateItem(
            StockInfo stock,
            PresetScanCode preset,
            CandidateSignalSnapshot snapshot,
            TradePlan trade,
            DateTime scanTime,
            decimal wishScore,
            decimal entryScore,
            decimal finalScore)
        {
            return new CandidateDetails
            {
                Ticker = stock.Ticker,
                PresetScanCode = preset.ScanCode,
                PresetDescription = preset.Description,

                EntryPrice = trade.Entry,
                ExitPrice = trade.Exit,
                StopLoss = trade.Stop,

                ProfitPercent = CalculatePercent(trade.Entry, trade.Exit),
                LossPercent = CalculatePercent(trade.Entry, trade.Stop),

                Score = finalScore,
                WeeklyScore = null,
                DailyScore = wishScore,
                EntryScore = entryScore,

                IsWishList = false,

                DistanceTo20dHigh = snapshot.Current.DistanceTo20dHigh,
                DistanceTo52wHigh = snapshot.Current.DistanceTo52wHigh,
                DailyRSI14 = snapshot.Current.DailyRSI14,

                ScanTimeMarket = scanTime,
                Notes = BuildCandidateNotes(snapshot)
            };
        }

        private static decimal CalculateAverageDollarVolumeDaily20(List<Candle> candles)
        {
            var dailyDollarVolumes = candles
                .GroupBy(x => x.Time.Date)
                .Select(g =>
                {
                    var ordered = g.OrderBy(x => x.Time).ToList();
                    var dayClose = ordered[^1].Close;
                    var dayVolume = ordered.Sum(x => x.Volume);
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

        private sealed class WishListContext
        {
            public required StockInfo Stock { get; init; }
            public required PresetScanCode Preset { get; init; }
            public required CandidateSignalSnapshot Snapshot { get; init; }
            public required TradePlan Trade { get; init; }
            public required DateTime ScanTime { get; init; }
            public required decimal AvgDollarVolumeDaily20 { get; init; }
            public required CandidateDetails WishListItem { get; init; }
        }
    }
}