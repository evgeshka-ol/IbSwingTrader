using System.Text.Json;
using System.Text.Json.Nodes;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class CandidateResultWriter(
        IAgentPathService pathService,
        IJsonFileService jsonFileService,
        IMarketSettingsProvider marketSettingsProvider,
        ITextLogger logger,
        IConsoleColorWriter console,
        ICompositePropertyJsonBuilder jsonBuilder,
        INumberTextFormatter fmt) : ICandidateResultWriter
    {
        private readonly IAgentPathService _pathService = pathService;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
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

            var existing = await LoadExistingCandidatesAsync(filePath);
            var merged = MergeCandidates(existing, candidates);
            var json = BuildJson(merged);
            await File.WriteAllTextAsync(filePath, json);

            _logger.Info($"Candidate results saved: {filePath}");
        }

        private async Task<List<CandidateDetails>> LoadExistingCandidatesAsync(string filePath)
        {
            if (File.Exists(filePath))
                return await _jsonFileService.ReadAsync<List<CandidateDetails>>(filePath) ?? [];

            var legacyFolder = _pathService.GetLegacyCandidatesFolder();
            if (!Directory.Exists(legacyFolder))
                return [];

            var result = new List<CandidateDetails>();

            foreach (var legacyFile in Directory
                         .GetFiles(legacyFolder, "*.json", SearchOption.TopDirectoryOnly)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var items = await _jsonFileService.ReadAsync<List<CandidateDetails>>(legacyFile);
                if (items != null && items.Count > 0)
                    result.AddRange(items);
            }

            if (result.Count > 0)
                _logger.Info($"Seeded aggregated candidates from legacy files: {result.Count}");

            return result;
        }

        private static List<CandidateDetails> MergeCandidates(
            List<CandidateDetails> existing,
            List<CandidateDetails> incoming)
        {
            var map = new Dictionary<string, CandidateDetails>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in existing)
                map[BuildCandidateKey(item)] = item;

            foreach (var item in incoming)
                map[BuildCandidateKey(item)] = item;

            return map.Values
                .OrderByDescending(x => x.Scan.ScanTimeMarket)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildCandidateKey(CandidateDetails candidate)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{candidate.Ticker}|{candidate.Scan.PresetScanCode}|{candidate.Scan.ScanTimeMarket:O}");
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
            return MarketTime.Now(timezoneId);
        }
    }
}
