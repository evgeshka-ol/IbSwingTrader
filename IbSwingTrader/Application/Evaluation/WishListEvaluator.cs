using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Application.Evaluation
{
    public class WishListEvaluator : IWishListEvaluator
    {
        private readonly IContractResolver _contractResolver;
        private readonly IHistoricalDataService _historicalDataService;
        private readonly ICandidateSignalAnalyzer _signalAnalyzer;
        private readonly IWishListEvaluationSettingsProvider _settingsProvider;
        private readonly ITextLogger _logger;

        public WishListEvaluator(
            IContractResolver contractResolver,
            IHistoricalDataService historicalDataService,
            ICandidateSignalAnalyzer signalAnalyzer,
            IWishListEvaluationSettingsProvider settingsProvider,
            ITextLogger logger)
        {
            _contractResolver = contractResolver;
            _historicalDataService = historicalDataService;
            _signalAnalyzer = signalAnalyzer;
            _settingsProvider = settingsProvider;
            _logger = logger;
        }

        public async Task<List<WishListEvaluationResult>> EvaluateAsync(List<WishListItem> items)
        {
            var results = new List<WishListEvaluationResult>();

            foreach (var item in items)
            {
                try
                {
                    var result = await EvaluateOneAsync(item);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Wish list evaluate failed for {item.Ticker}: {ex.Message}");

                    results.Add(new WishListEvaluationResult
                    {
                        Ticker = item.Ticker,
                        ScanTimeNy = item.Scan.ScanTimeMarket,
                        RemoveFromWishList = false,
                        Decision = "Keep",
                        Reason = $"CandidateEvaluation error: {ex.Message}"
                    });
                }
            }

            return results;
        }

        private async Task<WishListEvaluationResult> EvaluateOneAsync(WishListItem item)
        {
            var settings = _settingsProvider.Get();

            var contract = await _contractResolver.ResolveStockAsync(item.Ticker);

            var end = MarketTime.Now();
            var start = end.AddDays(-settings.HistoryDaysToLoad);

            var candles = await _historicalDataService.GetCandlesRange(
                item.Ticker,
                contract,
                Timeframe.H4,
                start,
                end);

            if (candles == null || candles.Count == 0)
            {
                _logger.Warning(
                    $"Wish list evaluation deferred for {item.Ticker}: no historical candles returned.");
                return Defer(item, "Historical data unavailable, deferred until next run");
            }

            if (candles.Count < 80)
            {
                return Remove(item, "Not enough fresh candles to re-evaluate wish list item");
            }

            var snapshot = _signalAnalyzer.Analyze(candles);
            var currentPrice = candles[^1].Close;

            var wishListPrice = item.Context.DistanceTo20dHigh switch
            {
                _ => 0m
            };

            var ageDays = (MarketTime.Now().Date - item.Scan.ScanTimeMarket.Date).TotalDays;

            var dropFromReferencePct = 0m;
            if (TryEstimateReferencePrice(item, out var referencePrice) && referencePrice > 0m)
                dropFromReferencePct = CalcPct(referencePrice, currentPrice);

            if (referencePrice > 0m &&
                dropFromReferencePct <= settings.MaxAdditionalDropFromWishListPct)
            {
                return Remove(
                    item,
                    $"Continued falling after wish list inclusion ({dropFromReferencePct:F2}%)");
            }

            var weakDaily =
                snapshot.Current.DailyRSI14 < settings.MinCurrentDailyRsi14 &&
                snapshot.Current.DailyMACDLineMinusSignal <= settings.MaxCurrentDailyMacdLineMinusSignal;

            var weakWeekly =
                snapshot.Current.WeeklyMaSignedDistancePct.HasValue &&
                snapshot.Current.WeeklyMaSignedDistancePct.Value <= settings.MinWeeklyMaSignedDistancePct;

            if (weakDaily && weakWeekly)
            {
                return Remove(
                    item,
                    "Daily and weekly context deteriorated");
            }

            var hasNoImprovement =
                snapshot.DailyMaDelta3 <= settings.MinDailyMaDelta3ToKeep &&
                snapshot.DailyRsiDelta3 <= settings.MinDailyRsiDelta3ToKeep &&
                snapshot.DailyMacdDelta3 <= settings.MinDailyMacdDelta3ToKeep;

            if (item.ExpectedBarsToTarget == null && hasNoImprovement)
            {
                return Remove(
                    item,
                    "No target forecast and no positive improvement signal");
            }

            if (ageDays >= settings.MaxDaysInWishListWithoutImprovement && hasNoImprovement)
            {
                return Remove(
                    item,
                    $"No improvement for too long ({ageDays:0} days)");
            }

            return Keep(item, "Still valid for monitoring");
        }

        private static bool TryEstimateReferencePrice(WishListItem item, out decimal price)
        {
            price = 0m;

            if (item.ExpectedTargetMarketTime.HasValue)
                return false;

            return false;
        }

        private static decimal CalcPct(decimal from, decimal to)
        {
            if (from == 0m)
                return 0m;

            return (to - from) / from * 100m;
        }

        private static WishListEvaluationResult Keep(WishListItem item, string reason)
        {
            return new WishListEvaluationResult
            {
                Ticker = item.Ticker,
                ScanTimeNy = item.Scan.ScanTimeMarket,
                RemoveFromWishList = false,
                Decision = "Keep",
                Reason = reason
            };
        }

        private static WishListEvaluationResult Defer(WishListItem item, string reason)
        {
            return new WishListEvaluationResult
            {
                Ticker = item.Ticker,
                ScanTimeNy = item.Scan.ScanTimeMarket,
                RemoveFromWishList = false,
                Decision = "Deferred",
                Reason = reason
            };
        }

        private static WishListEvaluationResult Remove(WishListItem item, string reason)
        {
            return new WishListEvaluationResult
            {
                Ticker = item.Ticker,
                ScanTimeNy = item.Scan.ScanTimeMarket,
                RemoveFromWishList = true,
                Decision = "Remove",
                Reason = reason
            };
        }
    }
}
