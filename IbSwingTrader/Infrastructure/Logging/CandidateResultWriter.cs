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

            var existingDocument = await LoadDocumentAsync(filePath);
            var merged = MergeCandidates(existingDocument.Candidates, candidates);
            var summary = BuildSummary(candidates);
            var json = BuildJson(summary, merged);
            await File.WriteAllTextAsync(filePath, json);

            _logger.Info($"Candidate results saved: {filePath}");
        }

        private static List<CandidateDetails> MergeCandidates(
            List<CandidateDetails> existing,
            List<CandidateDetails> incoming)
        {
            var map = new Dictionary<string, CandidateDetails>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in existing)
                UpsertCandidate(map, item, preferIncomingOnSameScan: true);

            foreach (var item in incoming)
                UpsertCandidate(map, item, preferIncomingOnSameScan: false);

            return map.Values
                .OrderByDescending(x => x.Scan.ScanTimeMarket)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void UpsertCandidate(
            Dictionary<string, CandidateDetails> map,
            CandidateDetails candidate,
            bool preferIncomingOnSameScan)
        {
            var key = BuildCandidateOperationKey(candidate);

            if (!map.TryGetValue(key, out var existing))
            {
                map[key] = candidate;
                return;
            }

            var keepIncoming =
                candidate.Scan.ScanTimeMarket < existing.Scan.ScanTimeMarket ||
                (candidate.Scan.ScanTimeMarket == existing.Scan.ScanTimeMarket &&
                 preferIncomingOnSameScan &&
                 candidate.Score.Score >= existing.Score.Score);

            if (keepIncoming)
                map[key] = candidate;
        }

        private static string BuildCandidateOperationKey(CandidateDetails candidate)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{candidate.Ticker}|{candidate.TradePlan.EntryPrice:G29}|{candidate.TradePlan.ExitPrice:G29}|{candidate.TradePlan.StopLoss:G29}");
        }

        private async Task<CandidateFileDocument> LoadDocumentAsync(string filePath)
        {
            if (!File.Exists(filePath))
                return new CandidateFileDocument();

            var json = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new CandidateFileDocument();

            var firstNonWhitespace = json.FirstOrDefault(x => !char.IsWhiteSpace(x));

            if (firstNonWhitespace == '[')
            {
                return new CandidateFileDocument
                {
                    Candidates = await _jsonFileService.ReadAsync<List<CandidateDetails>>(filePath) ?? []
                };
            }

            return await _jsonFileService.ReadAsync<CandidateFileDocument>(filePath) ?? new CandidateFileDocument();
        }

        private string BuildJson(
            IEnumerable<CandidateSummaryItem> summary,
            IEnumerable<CandidateDetails> candidates)
        {
            var root = new JsonObject();
            var summaryArray = new JsonArray();
            var candidatesArray = new JsonArray();

            foreach (var item in summary)
                summaryArray.Add(_jsonBuilder.BuildObject(item));

            foreach (var candidate in candidates)
                candidatesArray.Add(_jsonBuilder.BuildObject(candidate));

            root["Summary"] = summaryArray;
            root["Candidates"] = candidatesArray;

            return root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        private List<CandidateSummaryItem> BuildSummary(IEnumerable<CandidateDetails> candidates)
        {
            return candidates
                .OrderByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
                .ThenByDescending(x => x.Score.Score)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .Select(x =>
                {
                    var candidateType =
                        x.NeedsDeeperEntry ? " deep-entry" :
                        x.NeedsMomentumExit ? " momentum-exit" :
                        string.Empty;
                    var ticker =
                        $"{x.Ticker} " +
                        $"{_fmt.Price(x.TradePlan.EntryPrice)} " +
                        $"{_fmt.Price(x.TradePlan.ExitPrice)} " +
                        $"{_fmt.Price(x.TradePlan.StopLoss)} " +
                        $"{_fmt.Percent(x.TradePlan.ProfitPercent)}%/" +
                        $"{_fmt.Percent(x.TradePlan.LossPercent)}%" +
                        $" rank={_fmt.Generic(x.Score.NextDayRank ?? 0m)}" +
                        $"{candidateType}";

                    return new CandidateSummaryItem
                    {
                        Ticker = ticker,
                        Comment = string.Empty
                    };
                })
                .ToList();
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
