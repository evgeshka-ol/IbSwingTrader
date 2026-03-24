using System.Text.Json;
using System.Text.Json.Nodes;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class CandidateResultWriter(
        IMarketSettingsProvider marketSettingsProvider,
        ITextLogger logger,
        IConsoleColorWriter console,
        ICompositePropertyJsonBuilder jsonBuilder,
        INumberTextFormatter fmt) : ICandidateResultWriter
    {
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;
        private readonly ITextLogger _logger = logger;
        private readonly IConsoleColorWriter _console = console;
        private readonly ICompositePropertyJsonBuilder _jsonBuilder = jsonBuilder;
        private readonly INumberTextFormatter _fmt = fmt;

        public async Task WriteAsync(string filePath, List<CandidateDetails> candidates)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(candidates);

            var folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            var marketSettings = _marketSettingsProvider.Get();
            var scanTimeMarket = GetMarketNow(marketSettings.Timezone);

            foreach (var candidate in candidates)
            {
                candidate.Scan.ScanTimeMarket = scanTimeMarket;
                candidate.Scan.ScanTimeZone = marketSettings.Timezone;

                WriteCandidateToConsole(candidate);
            }

            var json = BuildJson(candidates);
            await File.WriteAllTextAsync(filePath, json);

            _logger.Info($"Candidate results saved: {filePath}");
        }

        private string BuildJson(IEnumerable<CandidateDetails> candidates)
        {
            var array = new JsonArray();

            foreach (var candidate in candidates)
                array.Add(_jsonBuilder.BuildObject(candidate));

            return array.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private void WriteCandidateToConsole(CandidateDetails candidate)
        {
            _console.Write($"{candidate.Ticker} ", ConsoleColor.Gray);
            _console.Write($"{_fmt.Price(candidate.TradePlan.EntryPrice)} ", ConsoleColor.DarkYellow);
            _console.Write($"{_fmt.Price(candidate.TradePlan.ExitPrice)} ", ConsoleColor.DarkGreen);
            _console.Write($"{_fmt.Price(candidate.TradePlan.StopLoss)} ", ConsoleColor.DarkRed);
            _console.Write($"{_fmt.Percent(candidate.TradePlan.ProfitPercent)}%", ConsoleColor.Green);
            _console.Write("/", ConsoleColor.DarkGray);
            _console.Write($"{_fmt.Percent(candidate.TradePlan.LossPercent)}%", ConsoleColor.Red);
            _console.WriteLine(string.Empty, ConsoleColor.Gray);
        }

        private static DateTime GetMarketNow(string timezoneId)
        {
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);
        }
    }
}