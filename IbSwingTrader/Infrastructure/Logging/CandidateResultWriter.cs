using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class CandidateResultWriter(
        IAgentPathService pathService,
        IMarketSettingsProvider marketSettingsProvider,
        ITextLogger logger,
        IConsoleColorWriter console,
        IObjectPropertyReader propertyReader,
        INumberTextFormatter fmt) : ICandidateResultWriter
    {
        private static readonly HashSet<string> PriceFields =
        [
            nameof(Candidate.EntryPrice),
            nameof(Candidate.ExitPrice),
            nameof(Candidate.StopLoss)
        ];

        private static readonly HashSet<string> PercentFields =
        [
            nameof(Candidate.ProfitPercent),
            nameof(Candidate.LossPercent)
        ];

        private static readonly HashSet<string> RatioFields =
        [
            nameof(CandidateDetails.Pullback10d),
            nameof(CandidateDetails.DistanceTo20dHigh),
            nameof(CandidateDetails.DistanceTo52wHigh),
            nameof(CandidateDetails.VolumeRatio20),
            nameof(CandidateDetails.ATRRatio),
            nameof(CandidateDetails.TrendPosition),
            nameof(CandidateDetails.DailyTrendPosition),
            nameof(CandidateDetails.DailyPullback10d),
            nameof(CandidateDetails.DailyRSI14),
            nameof(CandidateDetails.BBMidSignedDistancePct),
            nameof(CandidateDetails.WeeklyMACDHistDelta),
            nameof(CandidateDetails.Score)
        ];

        private readonly IAgentPathService _pathService = pathService;
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;
        private readonly ITextLogger _logger = logger;
        private readonly IConsoleColorWriter _console = console;
        private readonly IObjectPropertyReader _propertyReader = propertyReader;
        private readonly INumberTextFormatter _fmt = fmt;

        public async Task WriteAsync(List<CandidateDetails> candidates)
        {
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

        private JsonValue? ToJsonNode(object? value, string propertyName)
        {
            if (value == null)
                return null;

            return value switch
            {
                decimal d when PriceFields.Contains(propertyName)
                    => JsonValue.Create(_fmt.PriceValue(d)),

                decimal d when PercentFields.Contains(propertyName)
                    => JsonValue.Create(_fmt.PercentValue(d)),

                decimal d when RatioFields.Contains(propertyName)
                    => JsonValue.Create(_fmt.RatioValue(d)),

                decimal d
                    => JsonValue.Create(_fmt.GenericValue(d)),

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
            _console.Write($"{_fmt.Price(candidate.EntryPrice)} ", ConsoleColor.DarkYellow);
            _console.Write($"{_fmt.Price(candidate.ExitPrice)} ", ConsoleColor.DarkGreen);
            _console.Write($"{_fmt.Price(candidate.StopLoss)} ", ConsoleColor.DarkRed);
            _console.Write($"{_fmt.Percent(candidate.ProfitPercent)}%", ConsoleColor.Green);
            _console.Write("/", ConsoleColor.DarkGray);
            _console.WriteLine($"{_fmt.Percent(candidate.LossPercent)}%", ConsoleColor.Red);
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