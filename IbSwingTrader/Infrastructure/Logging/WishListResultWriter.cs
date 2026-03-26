using System.Text.Json;
using System.Text.Json.Nodes;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class WishListResultWriter(
        IMarketSettingsProvider marketSettingsProvider,
        IWishListReader wishListReader,
        IWishListMerger wishListMerger,
        ITextLogger logger,
        ICompositePropertyJsonBuilder jsonBuilder) : IWishListResultWriter
    {
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;
        private readonly IWishListReader _wishListReader = wishListReader;
        private readonly IWishListMerger _wishListMerger = wishListMerger;
        private readonly ITextLogger _logger = logger;
        private readonly ICompositePropertyJsonBuilder _jsonBuilder = jsonBuilder;

        public async Task WriteAsync(string filePath, List<WishListItem> items)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(items);

            var folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            var marketSettings = _marketSettingsProvider.Get();
            var marketNow = GetMarketNow(marketSettings.Timezone);

            foreach (var item in items)
            {
                item.Scan.ScanTimeMarket = marketNow;
                item.Scan.ScanTimeZone = marketSettings.Timezone;
            }

            var currentItems = await _wishListReader.ReadAsync(filePath);
            var mergedItems = _wishListMerger.Merge(currentItems, items, marketNow);

            var json = BuildJson(mergedItems);
            await File.WriteAllTextAsync(filePath, json);

            _logger.Info(
                $"Wish list saved: {filePath}. " +
                $"Current items: {currentItems.Count}, " +
                $"New items: {items.Count}, " +
                $"Merged items: {mergedItems.Count}");
        }

        private string BuildJson(IEnumerable<WishListItem> items)
        {
            var array = new JsonArray();

            foreach (var item in items)
                array.Add(_jsonBuilder.BuildObject(item));

            return array.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private static DateTime GetMarketNow(string timezoneId)
        {
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);
        }
    }
}