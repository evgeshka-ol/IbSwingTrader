using System.Text;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class WishListResultWriter(
        ITextLogger logger,
        IObjectPropertyReader objectPropertyReader) : IWishListResultWriter
    {
        private readonly ITextLogger _logger = logger;
        private readonly IObjectPropertyReader _propertyReader = objectPropertyReader;

        public async Task WriteAsync(string filePath, List<WishListItem> items)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(items);

            var csvPath = GetCsvPath(filePath);
            var folder = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            var table = WishListCsvTableBuilder.Build(items, _propertyReader);
            var sb = new StringBuilder();

            if (table.Headers.Count > 0)
                sb.AppendLine(string.Join(",", table.Headers.Select(EscapeCsv)));

            foreach (var row in table.Rows)
            {
                var values = table.Headers
                    .Select(header => row.TryGetValue(header, out var value) ? value : string.Empty)
                    .Select(EscapeCsv);

                sb.AppendLine(string.Join(",", values));
            }

            await File.WriteAllTextAsync(csvPath, sb.ToString(), Encoding.UTF8);

            var jsonPath = GetJsonPath(filePath);
            DeleteLegacyJsonIfPresent(csvPath, jsonPath);

            _logger.Info($"Wish list CSV saved: {csvPath}");
        }

        private static string GetCsvPath(string filePath)
        {
            return Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? filePath
                : Path.ChangeExtension(filePath, ".csv");
        }

        private static string GetJsonPath(string filePath)
        {
            return Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? filePath
                : Path.ChangeExtension(filePath, ".json");
        }

        private void DeleteLegacyJsonIfPresent(string csvPath, string jsonPath)
        {
            if (string.Equals(csvPath, jsonPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(jsonPath))
                return;

            File.Delete(jsonPath);
            _logger.Info($"Legacy wish list JSON migrated and deleted: {jsonPath}");
        }

        private static string EscapeCsv(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }
    }
}
