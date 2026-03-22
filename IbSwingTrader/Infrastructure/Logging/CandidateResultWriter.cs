using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class CandidateResultWriter : ICandidateResultWriter
    {
        private static readonly HashSet<string> RoundTo2Fields =
        [
            nameof(Candidate.EntryPrice),
            nameof(Candidate.ExitPrice),
            nameof(Candidate.StopLoss),
            nameof(Candidate.ProfitPercent),
            nameof(Candidate.LossPercent)
        ];

        private readonly IAgentPathService _pathService;
        private readonly IGetCandidatesSettingsProvider _getCandidatesSettingsProvider;
        private readonly IMarketSettingsProvider _marketSettingsProvider;
        private readonly ITextLogger _logger;
        private readonly IConsoleColorWriter _console;
        private readonly IObjectPropertyReader _propertyReader;

        public CandidateResultWriter(
            IAgentPathService pathService,
            IGetCandidatesSettingsProvider getCandidatesSettingsProvider,
            IMarketSettingsProvider marketSettingsProvider,
            ITextLogger logger,
            IConsoleColorWriter console,
            IObjectPropertyReader propertyReader)
        {
            _pathService = pathService;
            _getCandidatesSettingsProvider = getCandidatesSettingsProvider;
            _marketSettingsProvider = marketSettingsProvider;
            _logger = logger;
            _console = console;
            _propertyReader = propertyReader;
        }

        public async Task WriteAsync(List<CandidateDetails> candidates)
        {
            var settings = _getCandidatesSettingsProvider.Get();
            var marketSettings = _marketSettingsProvider.Get();

            var folder = _pathService.GetCandidatesFolder();
            Directory.CreateDirectory(folder);

            var scanTimeMarket = GetMarketNow(marketSettings.Timezone);
            var fileName = BuildFileName(scanTimeMarket);
            var filePath = Path.Combine(folder, fileName);

            foreach (var candidate in candidates)
            {
                candidate.ScanTimeMarket = scanTimeMarket;
                candidate.ScanTimeZone = marketSettings.Timezone;

                WriteCandidateToConsole(candidate);
            }

            var json = BuildJson(candidates);

            await File.WriteAllTextAsync(filePath, json);

            _logger.Info($"Candidates saved: {filePath}");
        }

        private string BuildJson(IEnumerable<CandidateDetails> candidates)
        {
            var array = new JsonArray();

            foreach (var candidate in candidates)
                array.Add(ToJsonObject(candidate));

            return array.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private JsonObject ToJsonObject<T>(T item)
        {
            var props = _propertyReader.GetOrderedProperties(typeof(T));
            var obj = new JsonObject();

            foreach (var prop in props)
            {
                var value = prop.GetValue(item);
                obj[prop.Name] = ToJsonNode(value, prop.Name);
            }

            return obj;
        }

        private static JsonNode? ToJsonNode(object? value, string propertyName)
        {
            if (value == null)
                return null;

            return value switch
            {
                decimal d when RoundTo2Fields.Contains(propertyName)
                    => JsonValue.Create(Math.Round(d, 2, MidpointRounding.AwayFromZero)),

                decimal d
                    => JsonValue.Create(decimal.Parse(
                        d.ToString("F6", CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture)),

                DateTime dt => JsonValue.Create(
                    propertyName.EndsWith("Date", StringComparison.OrdinalIgnoreCase)
                        ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),

                bool b => JsonValue.Create(b),
                int i => JsonValue.Create(i),
                long l => JsonValue.Create(l),
                double d => JsonValue.Create(d),
                float f => JsonValue.Create(f),

                _ => JsonValue.Create(value.ToString())
            };
        }

        private void WriteCandidateToConsole(Candidate candidate)
        {
            _console.Write($"{candidate.Ticker} ", ConsoleColor.Gray);
            _console.Write($"{candidate.EntryPrice:F2} ", ConsoleColor.DarkYellow);
            _console.Write($"{candidate.ExitPrice:F2} ", ConsoleColor.DarkGreen);
            _console.Write($"{candidate.StopLoss:F2} ", ConsoleColor.DarkRed);
            _console.Write($"{candidate.ProfitPercent:+0.00;-0.00}%", ConsoleColor.Green);
            _console.Write("/", ConsoleColor.DarkGray);
            _console.WriteLine($"{candidate.LossPercent:+0.00;-0.00}%", ConsoleColor.Red);
        }

        private static string BuildFileName(DateTime scanTimeMarket)
        {
            return $"candidates_{scanTimeMarket:yyyyMMdd_HHmm}_MARKET.json";
        }

        private static DateTime GetMarketNow(string timezoneId)
        {
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);
        }
    }
}