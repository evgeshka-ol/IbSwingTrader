using System.Text.Json;

namespace IbSwingTrader.Application.WishList
{
    public class WishListReader(
        IAgentPathService pathService,
        ITextLogger logger) : IWishListReader
    {
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITextLogger _logger = logger;

        private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        static WishListReader()
        {
            _jsonSerializerOptions.Converters.Add(new FlexibleDateTimeConverter());
            _jsonSerializerOptions.Converters.Add(new FlexibleNullableDateTimeConverter());
        }

        public async Task<List<WishListItem>> ReadAsync(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            if (!File.Exists(filePath))
            {
                _logger.Info($"Wish list file not found. Starting from empty state: {filePath}");
                return [];
            }

            var json = await File.ReadAllTextAsync(filePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.Info($"Wish list file is empty. Starting from empty state: {filePath}");
                return [];
            }

            json = NormalizeLegacyJson(json);

            var items = JsonSerializer.Deserialize<List<WishListItem>>(json, _jsonSerializerOptions);

            return items ?? [];
        }

        private static string NormalizeLegacyJson(string json)
        {
            return json
                .Replace("\"ScanTimeMarket\"", "\"ScanTime\"", StringComparison.Ordinal)
                .Replace("\"FirstSeenMarketTime\"", "\"FirstSeen\"", StringComparison.Ordinal)
                .Replace("\"LastEvaluatedMarketTime\"", "\"LastEvaluatedAt\"", StringComparison.Ordinal)
                .Replace("\"ExpectedTargetMarketTime\"", "\"ExpectedTargetTime\"", StringComparison.Ordinal)
                .Replace("\"LastStatusMarketTime\"", "\"LastStatusTime\"", StringComparison.Ordinal);
        }
    }
}
