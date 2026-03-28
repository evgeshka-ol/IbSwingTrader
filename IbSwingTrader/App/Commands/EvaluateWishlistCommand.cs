using IbSwingTrader.Common.Time;

namespace IbSwingTrader.App.Commands
{
    public class EvaluateWishlistCommand(
        ITwsConnection twsConnection,
        IWishListEvaluator wishListEvaluator,
        IJsonFileService jsonFileService,
        IWishListResultWriter wishListWriter,
        ITextLogger logger,
        IAgentPathService pathService,
        ITwsSettingsProvider twsSettingsProvider,
        IMarketSettingsProvider marketSettingsProvider) : ICommand
    {
        private readonly ITwsConnection _twsConnection = twsConnection;
        private readonly IWishListEvaluator _wishListEvaluator = wishListEvaluator;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly IWishListResultWriter _wishListWriter = wishListWriter;
        private readonly ITextLogger _logger = logger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITwsSettingsProvider _twsSettingsProvider = twsSettingsProvider;
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;

        public async Task RunAsync()
        {
            var twsSettings = _twsSettingsProvider.Get();
            var wishListPath = _pathService.GetWishListFile();

            EnsureConnected(twsSettings.ConnectTimeoutSeconds);

            await EvaluateWishListAsync(wishListPath);

            _logger.Info("Wish list evaluation pipeline completed.");
        }

        private async Task EvaluateWishListAsync(string wishListPath)
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
                if (item.Scan.ScanTimeMarket.Date >= todayMarketDate)
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

            var removeKeys = evaluations
                .Where(x => x.RemoveFromWishList)
                .Select(x => BuildWishListKey(x.Ticker, x.ScanTimeNy))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var keptOldItems = oldItems
                .Where(x => !removeKeys.Contains(BuildWishListKey(x.Ticker, x.Scan.ScanTimeMarket)))
                .ToList();

            var updatedItems = todayItems
                .Concat(keptOldItems)
                .OrderByDescending(x => x.Scan.ScanTimeMarket)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await _wishListWriter.WriteAsync(wishListPath, updatedItems);

            LogWishListSummary(evaluations, items.Count, updatedItems.Count);
        }

        private void EnsureConnected(int timeoutSeconds)
        {
            if (_twsConnection.IsConnected)
                return;

            _logger.Info("Connecting to TWS...");

            _twsConnection.Connect();

            var connected = _twsConnection.Ready.Task
                .Wait(TimeSpan.FromSeconds(timeoutSeconds));

            if (!connected || !_twsConnection.IsConnected)
                throw new InvalidOperationException("Failed to connect to TWS.");

            _logger.Info("TWS connected.");
        }

        private void LogWishListSummary(
            List<WishListEvaluationResult> results,
            int originalCount,
            int updatedCount)
        {
            var removed = results.Count(x => x.RemoveFromWishList);
            var kept = results.Count - removed;

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
                $"Wish list evaluation completed. Evaluated={results.Count} Kept={kept} Removed={removed} Before={originalCount} After={updatedCount} Reasons: {reasonsText}");
        }

        private static string BuildWishListKey(string ticker, DateTime scanTimeNy)
        {
            return $"{ticker}__{scanTimeNy:yyyyMMddHHmmss}";
        }

        private static DateTime GetMarketNow(string timezoneId)
        {
            return MarketTime.Now(timezoneId);
        }
    }
}
