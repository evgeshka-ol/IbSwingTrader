using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace IbSwingTrader.Application.Evaluation
{
    public class CandidateEvaluationCsvService(
        IAgentPathService pathService,
        ITextLogger logger) : ICandidateEvaluationCsvService
    {
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITextLogger _logger = logger;

        public async Task WriteAsync(string path, List<CandidateEvaluationResult> results)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            await RewriteRecordsAsync(path, results);
        }

        private async Task RewriteRecordsAsync(string path, List<CandidateEvaluationResult> newRecords)
        {
            var existingRecords = await ReadExistingRecordsAsync(path);
            var merged = existingRecords
                .Concat(newRecords)
                .GroupBy(BuildKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Last())
                .OrderBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(x => x.ScanTimeMarket)
                .ThenByDescending(x => x.EvaluatedAtMarketTime)
                .ToList();

            var properties = typeof(CandidateEvaluationResult)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.CanRead)
                .OrderBy(x => x.MetadataToken)
                .ToArray();

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", properties.Select(x => Escape(x.Name))));

            foreach (var record in merged)
            {
                var values = properties
                    .Select(x => FormatValue(x.GetValue(record)))
                    .Select(Escape);

                sb.AppendLine(string.Join(",", values));
            }

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
        }

        private async Task<List<CandidateEvaluationResult>> ReadExistingRecordsAsync(string path)
        {
            if (!File.Exists(path))
                return [];

            var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8);
            if (lines.Length <= 1)
                return [];

            var delimiter = DetectDelimiter(lines[0]);
            var headers = SplitCsvLine(lines[0], delimiter);
            var headerIndex = headers
                .Select((name, index) => new { name, index })
                .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

            var properties = typeof(CandidateEvaluationResult)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.CanWrite)
                .ToArray();

            var records = new List<CandidateEvaluationResult>();

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = SplitCsvLine(line, delimiter);
                var record = new CandidateEvaluationResult
                {
                    Ticker = string.Empty,
                    PresetScanCode = string.Empty
                };

                foreach (var property in properties)
                {
                    if (!headerIndex.TryGetValue(property.Name, out var index))
                        continue;

                    if (index >= values.Count)
                        continue;

                    var raw = values[index];
                    var parsed = ParseValue(property.PropertyType, raw);
                    property.SetValue(record, parsed);
                }

                if (string.IsNullOrWhiteSpace(record.Ticker) || string.IsNullOrWhiteSpace(record.PresetScanCode))
                    continue;

                if (record.EvaluatedAtMarketTime == default)
                    record.EvaluatedAtMarketTime = record.EvaluationEndTime ?? record.ScanTimeMarket;

                if (record.StrategyVersion <= 0)
                    record.StrategyVersion = InferStrategyVersion(record);

                records.Add(record);
            }

            return records;
        }

        private static object? ParseValue(Type type, string raw)
        {
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
            {
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                    return dt;

                return Nullable.GetUnderlyingType(type) != null ? null : default(DateTime);
            }

            if (targetType == typeof(DateTimeOffset))
            {
                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                    return dto;

                return Nullable.GetUnderlyingType(type) != null ? null : default(DateTimeOffset);
            }

            if (targetType == typeof(decimal))
            {
                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                    return dec;

                return Nullable.GetUnderlyingType(type) != null ? null : 0m;
            }

            if (targetType == typeof(double))
            {
                if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl))
                    return dbl;

                return Nullable.GetUnderlyingType(type) != null ? null : 0d;
            }

            if (targetType == typeof(float))
            {
                if (float.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var flt))
                    return flt;

                return Nullable.GetUnderlyingType(type) != null ? null : 0f;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
                    return i;

                return Nullable.GetUnderlyingType(type) != null ? null : 0;
            }

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(raw, out var b))
                    return b;

                return Nullable.GetUnderlyingType(type) != null ? null : false;
            }

            if (targetType.IsEnum)
                return Enum.Parse(targetType, raw, ignoreCase: true);

            return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
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

            return commaCount >= semicolonCount ? ',' : ';';
        }

        private static List<string> SplitCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
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
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            result.Add(sb.ToString());
            return result;
        }

        private static string BuildKey(CandidateEvaluationResult record)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{record.Ticker}|{record.PresetScanCode}|{record.ScanTimeMarket:O}|{record.EvaluatedAtMarketTime:O}");
        }

        private static int InferStrategyVersion(CandidateEvaluationResult record)
        {
            if (string.Equals(record.Ticker, "SGML", StringComparison.OrdinalIgnoreCase) &&
                record.ScanTimeMarket.Date == new DateTime(2026, 3, 28))
            {
                return 2;
            }

            return 1;
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
                return string.Empty;

            var type = value.GetType();

            if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
                return string.Empty;

            if (type == typeof(DateTime))
                return ((DateTime)value).ToString("O", CultureInfo.InvariantCulture);

            if (type == typeof(DateTimeOffset))
                return ((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture);

            if (type == typeof(decimal))
                return ((decimal)value).ToString(CultureInfo.InvariantCulture);

            if (type == typeof(double))
                return ((double)value).ToString(CultureInfo.InvariantCulture);

            if (type == typeof(float))
                return ((float)value).ToString(CultureInfo.InvariantCulture);

            if (type == typeof(bool))
                return (bool)value ? "true" : "false";

            if (type.IsEnum)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static string Escape(string? value)
        {
            value ??= string.Empty;

            if (value.Contains('"'))
                value = value.Replace("\"", "\"\"");

            if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value}\"";

            return value;
        }
    }
}
