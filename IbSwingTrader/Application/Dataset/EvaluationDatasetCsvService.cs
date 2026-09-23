using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace IbSwingTrader.Application.Dataset
{
    public class EvaluationDatasetCsvService(
        INumberTextFormatter numberFormatter,
        IArrayCellFormatter arrayCellFormatter,
        IObjectPropertyReader objectPropertyReader) : IEvaluationDatasetCsvService
    {
        private readonly INumberTextFormatter _fmt = numberFormatter;
        private readonly IArrayCellFormatter _arrayFmt = arrayCellFormatter;
        private readonly IObjectPropertyReader _propertyReader = objectPropertyReader;

        public async Task<List<EvaluationDatasetRow>> ReadAsync(string path)
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

            var properties = typeof(EvaluationDatasetRow)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.CanWrite)
                .ToArray();

            var rows = new List<EvaluationDatasetRow>();

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = SplitCsvLine(line, delimiter);
                var row = new EvaluationDatasetRow
                {
                    Ticker = string.Empty,
                    PresetScanCode = string.Empty,
                    GroupLabel = string.Empty
                };

                foreach (var property in properties)
                {
                    if (!headerIndex.TryGetValue(property.Name, out var index))
                        continue;

                    if (index >= values.Count)
                        continue;

                    var parsed = ParseValue(property.PropertyType, values[index]);
                    property.SetValue(row, parsed);
                }

                if (string.IsNullOrWhiteSpace(row.Ticker) ||
                    string.IsNullOrWhiteSpace(row.PresetScanCode))
                {
                    continue;
                }

                row.CandidateGroup = NormalizeCandidateGroup(row.CandidateGroup);

                rows.Add(row);
            }

            return rows;
        }

        private static string NormalizeCandidateGroup(string? groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return string.Empty;

            if (groupName.Equals("Runaway", StringComparison.OrdinalIgnoreCase) ||
                groupName.Equals("RunawayCandidates", StringComparison.OrdinalIgnoreCase) ||
                groupName.Equals("TodayResearchLikeCandidates", StringComparison.OrdinalIgnoreCase))
            {
                return "Runaway";
            }

            if (groupName.Equals("BellUp", StringComparison.OrdinalIgnoreCase))
                return "BellUp";

            if (groupName.Equals("Reversal", StringComparison.OrdinalIgnoreCase) ||
                groupName.Equals("ReversalCandidates", StringComparison.OrdinalIgnoreCase))
            {
                return "Reversal";
            }

            if (groupName.Equals("ReversalHook", StringComparison.OrdinalIgnoreCase))
                return "ReversalHook";

            return groupName;
        }

        public async Task WriteAsync(string path, List<EvaluationDatasetRow> rows)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(rows);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (rows.Count == 0)
            {
                await File.WriteAllTextAsync(path, string.Empty, Encoding.UTF8);
                return;
            }

            var properties = _propertyReader
                .GetOrderedProperties(typeof(EvaluationDatasetRow))
                .ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", properties.Select(x => Escape(x.Name))));

            foreach (var row in rows)
            {
                var values = properties
                    .Select(x => Escape(FormatValue(x.GetValue(row))))
                    .ToList();

                sb.AppendLine(string.Join(",", values));
            }

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
        }

        private string FormatValue(object? value)
        {
            if (value == null)
                return string.Empty;

            if (value is string s)
                return s;

            if (value is decimal d)
                return _fmt.Generic(d);

            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            if (value is bool b)
                return b ? "true" : "false";

            if (value is IEnumerable<decimal> decimalValues)
                return _arrayFmt.Format(decimalValues);

            if (value is IEnumerable enumerable && value is not string)
            {
                var decimals = new List<decimal>();

                foreach (var item in enumerable)
                {
                    if (item is decimal decimalItem)
                        decimals.Add(decimalItem);
                }

                return _arrayFmt.Format(decimals);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static object? ParseValue(Type type, string raw)
        {
            if (IsDecimalList(type))
                return ParseDecimalList(raw);

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (Nullable.GetUnderlyingType(type) != null)
                    return null;

                if (type == typeof(string))
                    return string.Empty;

                if (IsDecimalList(type))
                    return new List<decimal>();

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

            if (targetType == typeof(decimal))
            {
                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                    return dec;

                return Nullable.GetUnderlyingType(type) != null ? null : 0m;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    return value;

                return Nullable.GetUnderlyingType(type) != null ? null : 0;
            }

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(raw, out var value))
                    return value;

                return Nullable.GetUnderlyingType(type) != null ? null : false;
            }

            if (targetType == typeof(double))
            {
                if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    return value;

                return Nullable.GetUnderlyingType(type) != null ? null : 0d;
            }

            if (targetType == typeof(float))
            {
                if (float.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                    return value;

                return Nullable.GetUnderlyingType(type) != null ? null : 0f;
            }

            if (targetType.IsEnum)
                return Enum.Parse(targetType, raw, ignoreCase: true);

            return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
        }

        private static bool IsDecimalList(Type type)
        {
            return type == typeof(List<decimal>);
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

        private static string Escape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }
    }
}
