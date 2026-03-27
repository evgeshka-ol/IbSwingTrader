using System.Text.Json;
using System.Text.Json.Nodes;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class WishListResultWriter(
        ITextLogger logger,
        ICompositePropertyJsonBuilder jsonBuilder) : IWishListResultWriter
    {
        private readonly ITextLogger _logger = logger;
        private readonly ICompositePropertyJsonBuilder _jsonBuilder = jsonBuilder;

        public async Task WriteAsync(string filePath, List<WishListItem> items)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(items);

            var folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

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
    }
}