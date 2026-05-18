using IbSwingTrader.Common.Time;

namespace IbSwingTrader.App.Commands
{
    public class EvaluateWishlistCommand(
        ITwsConnection twsConnection,
        IWishListEvaluator wishListEvaluator,
        IJsonFileService jsonFileService,
        IWishListEvaluationCsvService wishListEvaluationCsvService,
        IWishListResultWriter wishListWriter,
        ITextLogger logger,
        IAgentPathService pathService,
        ITwsSettingsProvider twsSettingsProvider,
        IMarketSettingsProvider marketSettingsProvider) : ICommand
    {
        private readonly ITwsConnection _twsConnection = twsConnection;
        private readonly IWishListEvaluator _wishListEvaluator = wishListEvaluator;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly IWishListEvaluationCsvService _wishListEvaluationCsvService = wishListEvaluationCsvService;
        private readonly IWishListResultWriter _wishListWriter = wishListWriter;
        private readonly ITextLogger _logger = logger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITwsSettingsProvider _twsSettingsProvider = twsSettingsProvider;
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;

        public async Task RunAsync()
        {
            var twsSettings = _twsSettingsProvider.Get();
            var wishListPath = _pathService.GetWishListFile();
            var wishListEvaluationsPath = _pathService.GetWishListEvaluationsFile();

            EnsureConnected(twsSettings);

            await EvaluateWishListAsync(wishListPath, wishListEvaluationsPath);

            _logger.Info("Wish list evaluation pipeline completed.");
        }

        private async Task EvaluateWishListAsync(string wishListPath, string wishListEvaluationsPath)
        {
            if (!File.Exists(wishListPath))
            {
                _logger.Info("Wish list file not found. Skipping wish list evaluation.");
                return;
            }

            var items = await _jsonFileService.ReadAsync<List<WishListItem>>(wishListPath);

            if (items == null || items.Count == 0)
            {
                _logger.Info("Wish list is empty. Skipping wish list evaluation.");
                return;
            }

            var marketNow = GetMarketNow(_marketSettingsProvider.Get().Timezone);
            var todayMarketDate = marketNow.Date;

            var todayItems = new List<WishListItem>();
            var oldItems = new List<WishListItem>();

            foreach (var item in items)
            {
                if (item.Scan.ScanTime.Date >= todayMarketDate)
                    todayItems.Add(item);
                else
                    oldItems.Add(item);
            }

            _logger.Info(
                $"Wish list items loaded: total={items.Count}, today={todayItems.Count}, old={oldItems.Count}");

            if (oldItems.Count == 0)
            {
                _logger.Info("No old wish list items to evaluate.");
                return;
            }

            var evaluations = await _wishListEvaluator.EvaluateAsync(oldItems);
            var evaluationTime = marketNow;

            var evaluationMap = evaluations.ToDictionary(
                x => BuildWishListKey(x.Ticker, x.ScanTime),
                x => x,
                StringComparer.OrdinalIgnoreCase);

            var records = oldItems
                .Select(x => BuildRecord(x, evaluationMap, evaluationTime))
                .Where(x => x != null)
                .Cast<WishListEvaluationRecord>()
                .ToList();

            var updatedOldItems = oldItems
                .Select(x => ApplyEvaluationResult(x, evaluationMap, evaluationTime))
                .ToList();

            var updatedItems = todayItems
                .Concat(updatedOldItems)
                .OrderByDescending(x => x.Scan.ScanTime)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (records.Count > 0)
                await _wishListEvaluationCsvService.WriteAsync(wishListEvaluationsPath, records);

            await _wishListWriter.WriteAsync(wishListPath, updatedItems);

            LogWishListSummary(evaluations, items.Count, updatedItems.Count);
        }

        private void EnsureConnected(TwsSettings twsSettings)
        {
            if (_twsConnection.IsConnected)
                return;

            _logger.Info("Connecting to TWS...");

            _twsConnection.Connect(twsSettings.Host, twsSettings.Port, twsSettings.ClientId);

            var connected = _twsConnection.Ready.Task
                .Wait(TimeSpan.FromSeconds(twsSettings.ConnectTimeoutSeconds));

            if (!connected || !_twsConnection.IsConnected)
                throw new InvalidOperationException("Failed to connect to TWS.");

            _logger.Info("TWS connected.");
        }

        private void LogWishListSummary(
            List<WishListEvaluationResult> results,
            int originalCount,
            int updatedCount)
        {
            var removeSuggested = results.Count(x => x.RemoveFromWishList);
            var deferred = results.Count(x => string.Equals(x.Decision, "Deferred", StringComparison.OrdinalIgnoreCase));
            var kept = results.Count - removeSuggested - deferred;

            var groupedReasons = results
                .Where(x => x.RemoveFromWishList)
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Reason) ? "(no reason)" : x.Reason!)
                .OrderByDescending(x => x.Count())
                .Select(x => $"{x.Key}={x.Count()}")
                .ToList();

            var reasonsText = groupedReasons.Count == 0
                ? "none"
                : string.Join(", ", groupedReasons);

            _logger.Info(
                $"Wish list evaluation completed. Evaluated={results.Count} Kept={kept} Deferred={deferred} RemoveSuggested={removeSuggested} Before={originalCount} After={updatedCount} Reasons: {reasonsText}");
        }

        private static string BuildWishListKey(string ticker, DateTime scanTimeMarket)
        {
            return $"{ticker}__{scanTimeMarket:yyyyMMddHHmmss}";
        }

        private static WishListItem ApplyEvaluationResult(
            WishListItem item,
            Dictionary<string, WishListEvaluationResult> evaluationMap,
            DateTime evaluationTime)
        {
            if (!evaluationMap.TryGetValue(BuildWishListKey(item.Ticker, item.Scan.ScanTime), out var evaluation))
                return item;

            item.LastEvaluatedAt = evaluationTime;
            item.LastStatus = evaluation.Decision;
            item.LastStatusReason = evaluation.Reason;
            item.LastStatusTime = evaluationTime;

            return item;
        }

        private static WishListEvaluationRecord? BuildRecord(
            WishListItem item,
            Dictionary<string, WishListEvaluationResult> evaluationMap,
            DateTime evaluationTime)
        {
            if (!evaluationMap.TryGetValue(BuildWishListKey(item.Ticker, item.Scan.ScanTime), out var evaluation))
                return null;

            return new WishListEvaluationRecord
            {
                Ticker = item.Ticker,
                ScanTime = item.Scan.ScanTime,
                FirstSeen = item.FirstSeen,
                PreviousLastEvaluatedAt = item.LastEvaluatedAt,
                PreviousDecision = item.LastStatus,
                PreviousReason = item.LastStatusReason,
                EvaluatedAt = evaluationTime,
                Decision = evaluation.Decision,
                Reason = evaluation.Reason,
                RemoveSuggested = evaluation.RemoveFromWishList
            };
        }

        private static DateTime GetMarketNow(string timezoneId)
        {
            return MarketTime.Now(timezoneId);
        }
    }
}
