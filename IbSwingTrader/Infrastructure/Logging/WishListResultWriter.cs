using System.Text.Json;
using System.Text.Json.Nodes;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class WishListResultWriter(
        IMarketSettingsProvider marketSettingsProvider,
        ITextLogger logger,
        ICompositePropertyJsonBuilder jsonBuilder) : IWishListResultWriter
    {
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;
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
            var scanTimeMarket = GetMarketNow(marketSettings.Timezone);

            foreach (var item in items)
            {
                item.Scan.ScanTimeMarket = scanTimeMarket;
                item.Scan.ScanTimeZone = marketSettings.Timezone;
            }

            var json = BuildJson(items);
            await File.WriteAllTextAsync(filePath, json);

            _logger.Info($"Wish list saved: {filePath}");
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