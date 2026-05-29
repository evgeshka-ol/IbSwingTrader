using System.Globalization;
using System.Text;
using System.Text.Json;

namespace IbSwingTrader.Application.WishList
{
    public class WishListReader(
        IObjectPropertyReader objectPropertyReader,
        ITextLogger logger) : IWishListReader
    {
        private static readonly JsonSerializerOptions JsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IObjectPropertyReader _propertyReader = objectPropertyReader;
        private readonly ITextLogger _logger = logger;

        static WishListReader()
        {
            JsonSerializerOptions.Converters.Add(new FlexibleDateTimeConverter());
            JsonSerializerOptions.Converters.Add(new FlexibleNullableDateTimeConverter());
        }

        public async Task<List<WishListItem>> ReadAsync(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var csvPath = GetCsvPath(filePath);
            var jsonPath = GetJsonPath(filePath);

            if (File.Exists(csvPath) && new FileInfo(csvPath).Length > 0)
            {
                DeleteLegacyJsonIfPresent(csvPath, jsonPath);
                return await ReadCsvAsync(csvPath);
            }

            if (!File.Exists(jsonPath))
            {
                _logger.Info($"Wish list file not found. Starting from empty state: {csvPath}");
                return [];
            }

            var items = await ReadLegacyJsonAsync(jsonPath);
            await WriteCsvAsync(csvPath, items);
            DeleteLegacyJsonIfPresent(csvPath, jsonPath);

            return items;
        }

        private async Task<List<WishListItem>> ReadCsvAsync(string csvPath)
        {
            var lines = await File.ReadAllLinesAsync(csvPath, Encoding.UTF8);
            if (lines.Length <= 1)
                return [];

            var delimiter = DetectDelimiter(lines[0]);
            var headers = SplitCsvLine(lines[0], delimiter);
            var items = new List<WishListItem>();

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = SplitCsvLine(line, delimiter);
                var row = headers
                    .Select((header, index) => new { header, value = index < values.Count ? values[index] : string.Empty })
                    .ToDictionary(x => x.header, x => x.value, StringComparer.OrdinalIgnoreCase);

                items.Add(BuildItem(row));
            }

            return items;
        }

        private WishListItem BuildItem(IReadOnlyDictionary<string, string> row)
        {
            var item = new WishListItem
            {
                Ticker = row.GetValueOrDefault("Ticker") ?? string.Empty,
                Scan = new ScanInfo(),
                Score = new ScoreInfo(),
                Context = new MarketContextInfo()
            };

            SetObjectProperties(item.Scan, row, string.Empty);
            SetObjectProperties(item.Score, row, "Score");
            SetObjectProperties(item.Context, row, "Context");
            SetDirectProperties(item, row);

            return item;
        }

        private void SetDirectProperties(WishListItem item, IReadOnlyDictionary<string, string> row)
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                nameof(WishListItem.Ticker),
                nameof(WishListItem.Scan),
                nameof(WishListItem.Score),
                nameof(WishListItem.Context)
            };

            foreach (var property in _propertyReader.GetOrderedProperties(typeof(WishListItem)))
            {
                if (skip.Contains(property.Name) ||
                    !property.CanWrite ||
                    !row.TryGetValue(property.Name, out var raw))
                {
                    continue;
                }

                property.SetValue(item, ParseValue(property.PropertyType, raw));
            }
        }

        private void SetObjectProperties(object target, IReadOnlyDictionary<string, string> row, string prefix)
        {
            foreach (var property in _propertyReader.GetOrderedProperties(target.GetType()))
            {
                if (!property.CanWrite)
                    continue;

                var name = string.IsNullOrWhiteSpace(prefix)
                    ? property.Name
                    : $"{prefix}{property.Name}";

                if (!row.TryGetValue(name, out var raw))
                    continue;

                property.SetValue(target, ParseValue(property.PropertyType, raw));
            }
        }

        private static object? ParseValue(Type type, string raw)
        {
            if (type == typeof(List<decimal>))
                return ParseDecimalList(raw);

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (Nullable.GetUnderlyingType(type) != null)
                    return null;

                if (type == typeof(string))
                    return string.Empty;

                return Activator.CreateInstance(type);
            }

            var targetType = Nullable.GetUnderlyingType(type) ?? type;

            if (targetType == typeof(string))
                return raw;

            if (targetType == typeof(DateTime))
                return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                    ? dt
                    : default;

            if (targetType == typeof(decimal))
                return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec) ? dec : 0m;

            if (targetType == typeof(int))
                return int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var i) ? i : 0;

            if (targetType == typeof(bool))
                return bool.TryParse(raw, out var b) && b;

            if (targetType.IsEnum)
                return Enum.Parse(targetType, raw, ignoreCase: true);

            return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
        }

        private static List<decimal> ParseDecimalList(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return [];

            var trimmed = raw.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                trimmed = trimmed[1..^1];

            if (string.IsNullOrWhiteSpace(trimmed))
                return [];

            return
            [
                .. trimmed
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => decimal.TryParse(x, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                        ? value
                        : 0m)
            ];
        }

        private async Task<List<WishListItem>> ReadLegacyJsonAsync(string jsonPath)
        {
            var json = await File.ReadAllTextAsync(jsonPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.Info($"Wish list file is empty. Starting from empty state: {jsonPath}");
                return [];
            }

            json = NormalizeLegacyJson(json);
            return JsonSerializer.Deserialize<List<WishListItem>>(json, JsonSerializerOptions) ?? [];
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

        private async Task WriteCsvAsync(string csvPath, List<WishListItem> items)
        {
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
            _logger.Info($"Wish list CSV migrated: {csvPath}");
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

        private static char DetectDelimiter(string line)
        {
            var commaCount = 0;
            var semicolonCount = 0;
            var inQuotes = false;

            foreach (var c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (inQuotes)
                    continue;

                if (c == ',')
                    commaCount++;
                else if (c == ';')
                    semicolonCount++;
            }

            return semicolonCount > commaCount ? ';' : ',';
        }

        private static List<string> SplitCsvLine(string line, char delimiter)
        {
            var values = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == delimiter && !inQuotes)
                {
                    values.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            values.Add(sb.ToString());
            return values;
        }

        private static string EscapeCsv(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }
    }
}
