using System.Text.Json;

namespace IbSwingTrader.Application.WishList
{
    public class WishListReader(
        ITextLogger logger) : IWishListReader
    {
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

            var items = JsonSerializer.Deserialize<List<WishListItem>>(json, _jsonSerializerOptions);

            return items ?? [];
        }
    }
}