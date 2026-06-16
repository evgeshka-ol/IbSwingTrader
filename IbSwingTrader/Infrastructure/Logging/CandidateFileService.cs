using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IbSwingTrader.Infrastructure.Serialization;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class CandidateFileService(
        ICandidateCsvRowBuilder candidateCsvRowBuilder,
        IObjectPropertyReader objectPropertyReader,
        ITextLogger logger) : ICandidateFileService
    {
        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ICandidateCsvRowBuilder _candidateCsvRowBuilder = candidateCsvRowBuilder;
        private readonly IObjectPropertyReader _propertyReader = objectPropertyReader;
        private readonly ITextLogger _logger = logger;

        static CandidateFileService()
        {
            ReadOptions.Converters.Add(new FlexibleDateTimeConverter());
            ReadOptions.Converters.Add(new FlexibleNullableDateTimeConverter());
        }

        public async Task<CandidateFileDocument> ReadAsync(string candidatesPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(candidatesPath);

            var csvPath = GetCsvPath(candidatesPath);
            var jsonPath = GetJsonPath(candidatesPath);
            if (File.Exists(csvPath) && new FileInfo(csvPath).Length > 0)
            {
                DeleteLegacyJsonIfPresent(csvPath, jsonPath);
                return (await ReadCsvAsync(csvPath)).Document;
            }

            if (!File.Exists(jsonPath))
                return new CandidateFileDocument();

            var document = await ReadLegacyJsonAsync(jsonPath);
            await WriteAsync(candidatesPath, document, []);

            return document;
        }

        public async Task WriteAsync(
            string candidatesPath,
            CandidateFileDocument document,
            IEnumerable<CandidateDetails> currentScanOutput)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(candidatesPath);
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(currentScanOutput);

            var csvPath = GetCsvPath(candidatesPath);
            var folder = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            var currentOperationKeys = currentScanOutput
                .Select(CandidateCsvRowBuilder.BuildCandidateOperationKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            await WriteCsvAsync(candidatesPath, document, currentOperationKeys);
        }

        public async Task NormalizeAsync(string candidatesPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(candidatesPath);

            var csvPath = GetCsvPath(candidatesPath);
            if (File.Exists(csvPath) && new FileInfo(csvPath).Length > 0)
            {
                var result = await ReadCsvAsync(csvPath);
                await WriteCsvAsync(candidatesPath, result.Document, result.CurrentOperationKeys);
                return;
            }

            var document = await ReadAsync(candidatesPath);
            await WriteAsync(candidatesPath, document, []);
        }

        private async Task WriteCsvAsync(
            string candidatesPath,
            CandidateFileDocument document,
            IReadOnlySet<string> currentOperationKeys)
        {
            var csvPath = GetCsvPath(candidatesPath);
            var folder = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            var table = _candidateCsvRowBuilder.Build(document.Candidates, document.SameDayCandidates, currentOperationKeys);
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

            var jsonPath = GetJsonPath(candidatesPath);
            DeleteLegacyJsonIfPresent(csvPath, jsonPath);

            _logger.Info($"Candidate CSV results saved: {csvPath}");
        }

        private async Task<CandidateCsvReadResult> ReadCsvAsync(string csvPath)
        {
            var lines = await File.ReadAllLinesAsync(csvPath, Encoding.UTF8);
            if (lines.Length <= 1)
            {
                return new CandidateCsvReadResult(
                    new CandidateFileDocument(),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            var delimiter = DetectDelimiter(lines[0]);
            var headers = SplitCsvLine(lines[0], delimiter);
            var document = new CandidateFileDocument();
            var currentOperationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = SplitCsvLine(line, delimiter);
                var row = headers
                    .Select((header, index) => new { header, value = index < values.Count ? values[index] : string.Empty })
                    .ToDictionary(x => x.header, x => x.value, StringComparer.OrdinalIgnoreCase);
                var candidate = BuildCandidate(row);
                var group = row.GetValueOrDefault("CandidateGroup") ?? string.Empty;
                var isCurrentScanOutput =
                    row.TryGetValue("IsCurrentScanOutput", out var currentRaw) &&
                    bool.TryParse(currentRaw, out var current) &&
                    current;

                if (group.Equals("Runaway", StringComparison.OrdinalIgnoreCase) ||
                    group.Equals("RunawayCandidates", StringComparison.OrdinalIgnoreCase) ||
                    group.Equals("TodayResearchLikeCandidates", StringComparison.OrdinalIgnoreCase))
                {
                    document.SameDayCandidates.Add(candidate);
                }
                else
                {
                    document.Candidates.Add(candidate);
                }

                if (isCurrentScanOutput)
                    currentOperationKeys.Add(CandidateCsvRowBuilder.BuildCandidateOperationKey(candidate));
            }

            return new CandidateCsvReadResult(document, currentOperationKeys);
        }

        private CandidateDetails BuildCandidate(IReadOnlyDictionary<string, string> row)
        {
            var candidate = new CandidateDetails
            {
                Ticker = row.GetValueOrDefault("Ticker") ?? string.Empty,
                Scan = new ScanInfo(),
                TradePlan = new TradePlanInfo(),
                Score = new ScoreInfo(),
                Context = new MarketContextInfo(),
                CandidateSource = row.GetValueOrDefault(nameof(CandidateDetails.CandidateSource)) ?? string.Empty
            };

            SetObjectProperties(candidate.Scan, row, string.Empty);
            SetObjectProperties(candidate.TradePlan, row, "TradePlan");
            SetObjectProperties(candidate.Score, row, "Score");
            SetObjectProperties(candidate.Context, row, "Context");
            SetCandidateDirectProperties(candidate, row);

            if (HasPrefixedValue(row, "Diagnostics"))
            {
                candidate.Diagnostics = new CandidateDiagnostics();
                SetObjectProperties(candidate.Diagnostics, row, "Diagnostics");
            }

            return candidate;
        }

        private void SetCandidateDirectProperties(CandidateDetails candidate, IReadOnlyDictionary<string, string> row)
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                nameof(CandidateDetails.Ticker),
                nameof(CandidateDetails.Scan),
                nameof(CandidateDetails.TradePlan),
                nameof(CandidateDetails.Score),
                nameof(CandidateDetails.Context),
                nameof(CandidateDetails.Diagnostics)
            };

            foreach (var property in _propertyReader.GetOrderedProperties(typeof(CandidateDetails)))
            {
                if (skip.Contains(property.Name) ||
                    !property.CanWrite ||
                    !row.TryGetValue(property.Name, out var raw))
                {
                    continue;
                }

                property.SetValue(candidate, ParseValue(property.PropertyType, raw));
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

        private async Task<CandidateFileDocument> ReadLegacyJsonAsync(string jsonPath)
        {
            var json = await File.ReadAllTextAsync(jsonPath);
            if (string.IsNullOrWhiteSpace(json))
                return new CandidateFileDocument();

            var firstNonWhitespace = json.FirstOrDefault(x => !char.IsWhiteSpace(x));
            if (firstNonWhitespace == '[')
            {
                return new CandidateFileDocument
                {
                    Candidates = JsonSerializer.Deserialize<List<CandidateDetails>>(json, ReadOptions) ?? []
                };
            }

            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null)
                return new CandidateFileDocument();

            return new CandidateFileDocument
            {
                Candidates =
                    root["ReversalData"]?.Deserialize<List<CandidateDetails>>(ReadOptions) ??
                    root["ReversalCandidatesData"]?.Deserialize<List<CandidateDetails>>(ReadOptions) ??
                    root["Candidates"]?.Deserialize<List<CandidateDetails>>(ReadOptions) ??
                    [],
                SameDayCandidates =
                    root["RunawayData"]?.Deserialize<List<CandidateDetails>>(ReadOptions) ??
                    root["RunawayCandidatesData"]?.Deserialize<List<CandidateDetails>>(ReadOptions) ??
                    root["TodayResearchLikeCandidatesData"]?.Deserialize<List<CandidateDetails>>(ReadOptions) ??
                    root["SameDayCandidates"]?.Deserialize<List<CandidateDetails>>(ReadOptions) ??
                    []
            };
        }

        private static bool HasPrefixedValue(IReadOnlyDictionary<string, string> row, string prefix)
        {
            return row.Any(x =>
                x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(x.Value));
        }

        private static string GetCsvPath(string candidatesPath)
        {
            return Path.GetExtension(candidatesPath).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? candidatesPath
                : Path.ChangeExtension(candidatesPath, ".csv");
        }

        private static string GetJsonPath(string candidatesPath)
        {
            return Path.GetExtension(candidatesPath).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? candidatesPath
                : Path.ChangeExtension(candidatesPath, ".json");
        }

        private void DeleteLegacyJsonIfPresent(string csvPath, string jsonPath)
        {
            if (string.Equals(csvPath, jsonPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(jsonPath))
                return;

            File.Delete(jsonPath);
            _logger.Info($"Legacy candidate JSON migrated and deleted: {jsonPath}");
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

        private sealed record CandidateCsvReadResult(
            CandidateFileDocument Document,
            IReadOnlySet<string> CurrentOperationKeys);
    }
}
