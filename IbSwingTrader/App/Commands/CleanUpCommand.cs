using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IbSwingTrader.Common.Time;
using IbSwingTrader.Domain.Settings;

namespace IbSwingTrader.App.Commands
{
    public class CleanUpCommand(
        ICandidateEvaluationCsvService candidateEvaluationCsvService,
        IEvaluationDatasetBuilder evaluationDatasetBuilder,
        IJsonFileService jsonFileService,
        INumberTextFormatter fmt,
        ITextLogger logger,
        IAgentPathService pathService,
        ICleanUpSettingsProvider cleanUpSettingsProvider,
        IGetCandidatesSettingsProvider getCandidatesSettingsProvider,
        IMarketSettingsProvider marketSettingsProvider) : ICommand
    {
        private readonly ICandidateEvaluationCsvService _candidateEvaluationCsvService = candidateEvaluationCsvService;
        private readonly IEvaluationDatasetBuilder _evaluationDatasetBuilder = evaluationDatasetBuilder;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly INumberTextFormatter _fmt = fmt;
        private readonly ITextLogger _logger = logger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ICleanUpSettingsProvider _cleanUpSettingsProvider = cleanUpSettingsProvider;
        private readonly IGetCandidatesSettingsProvider _getCandidatesSettingsProvider = getCandidatesSettingsProvider;
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;

        public async Task RunAsync()
        {
            var settings = _cleanUpSettingsProvider.Get();
            var marketNow = MarketTime.Now(_marketSettingsProvider.Get().Timezone);

            var candidatesRemoved = await CleanCandidatesAsync(settings);
            var evaluationsRemoved = await CleanEvaluationsAsync(settings);
            await SyncEvaluationDatasetAsync(evaluationsRemoved > 0);
            var wishListRemoved = await CleanWishListAsync(settings, marketNow);
            var deletedFiles = CleanOldFiles(settings, marketNow);

            _logger.Info(
                $"Clean-up completed. CandidatesRemoved={candidatesRemoved} EvaluationsRemoved={evaluationsRemoved} WishListRemoved={wishListRemoved} FilesDeleted={deletedFiles}");
        }

        private async Task<int> CleanCandidatesAsync(CleanUpSettings settings)
        {
            if (!settings.RemoveEvaluatedCandidates)
                return 0;

            var candidatesPath = _pathService.GetCandidatesFile();
            var evaluationsPath = _pathService.GetEvaluationsFile();

            var candidateDocument = await LoadCandidateDocumentAsync(candidatesPath);
            var candidates = candidateDocument.Candidates;
            if (candidates.Count == 0 || !File.Exists(evaluationsPath))
                return 0;

            var removableOutcomes = settings.CandidateOutcomesToRemove
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (removableOutcomes.Count == 0)
                return 0;

            var evaluatedKeys = await LoadCandidateKeysByOutcomeAsync(evaluationsPath, removableOutcomes);
            if (evaluatedKeys.Count == 0)
                return 0;

            var filtered = candidates
                .Where(x => !evaluatedKeys.Contains(BuildCandidateKey(x.Ticker, x.Scan.PresetScanCode, x.Scan.ScanTime)))
                .ToList();

            var removed = candidates.Count - filtered.Count;
            var rebuiltSummary = BuildSummary(filtered);
            var summaryChanged = !AreSummariesEqual(candidateDocument.Summary, rebuiltSummary);

            if (removed <= 0 && !summaryChanged)
                return 0;

            candidateDocument.Candidates = filtered;
            candidateDocument.Summary = rebuiltSummary;
            await _jsonFileService.WriteAsync(candidatesPath, candidateDocument);

            _logger.Info(
                $"Candidates cleaned: removed={removed}, kept={filtered.Count}, summaryChanged={summaryChanged}, outcomes=[{string.Join(", ", removableOutcomes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}]");

            return removed;
        }

        private async Task SyncEvaluationDatasetAsync(bool evaluationsChanged)
        {
            if (!evaluationsChanged)
                return;

            var evaluationsPath = _pathService.GetEvaluationsFile();
            var datasetPath = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), "datasets", "evaluation-dataset.csv"));

            var evaluations = await _candidateEvaluationCsvService.ReadAsync(evaluationsPath);
            if (evaluations.Count > 0)
            {
                await _evaluationDatasetBuilder.RunAsync();
                return;
            }

            if (!File.Exists(datasetPath))
                return;

            File.Delete(datasetPath);
            _logger.Info($"Evaluation dataset deleted: {datasetPath}");
        }

        private async Task<int> CleanEvaluationsAsync(CleanUpSettings settings)
        {
            if (!settings.RemoveEvaluationReportRows)
                return 0;

            var evaluationsPath = _pathService.GetEvaluationsFile();
            if (!File.Exists(evaluationsPath))
                return 0;

            var removableOutcomes = settings.EvaluationOutcomesToRemove
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (removableOutcomes.Count == 0)
                return 0;

            var records = await _candidateEvaluationCsvService.ReadAsync(evaluationsPath);
            if (records.Count == 0)
                return 0;

            var filtered = records
                .Where(x => !removableOutcomes.Contains(x.Outcome ?? string.Empty))
                .ToList();

            var removed = records.Count - filtered.Count;
            if (removed <= 0)
                return 0;

            await _candidateEvaluationCsvService.WriteAsync(evaluationsPath, filtered);

            _logger.Info(
                $"Evaluation report cleaned: removed={removed}, kept={filtered.Count}, outcomes=[{string.Join(", ", removableOutcomes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}]");

            return removed;
        }

        private async Task<CandidateFileDocument> LoadCandidateDocumentAsync(string candidatesPath)
        {
            if (!File.Exists(candidatesPath))
                return new CandidateFileDocument();

            var json = await File.ReadAllTextAsync(candidatesPath);
            if (string.IsNullOrWhiteSpace(json))
                return new CandidateFileDocument();

            var firstNonWhitespace = json.FirstOrDefault(x => !char.IsWhiteSpace(x));

            if (firstNonWhitespace == '[')
            {
                var candidates = await _jsonFileService.ReadAsync<List<CandidateDetails>>(candidatesPath) ?? [];
                return new CandidateFileDocument
                {
                    Candidates = candidates
                };
            }

            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null)
                return new CandidateFileDocument();

            return new CandidateFileDocument
            {
                Candidates =
                    root["ReversalCandidatesData"]?.Deserialize<List<CandidateDetails>>() ??
                    root["Candidates"]?.Deserialize<List<CandidateDetails>>() ??
                    [],
                SameDayCandidates =
                    root["TodayResearchLikeCandidatesData"]?.Deserialize<List<CandidateDetails>>() ??
                    root["SameDayCandidates"]?.Deserialize<List<CandidateDetails>>() ??
                    []
            };
        }

        private CandidateSummarySections BuildSummary(List<CandidateDetails> candidates)
        {
            if (candidates.Count == 0)
                return new CandidateSummarySections();

            var latestScanTime = candidates.Max(x => x.Scan.ScanTime);

            return new CandidateSummarySections
            {
                ReversalCandidates = candidates
                    .Where(x => x.Scan.ScanTime == latestScanTime)
                    .OrderByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
                    .ThenByDescending(x => x.TradePlan.ProfitPercent)
                    .ThenByDescending(x => x.Score.Score)
                    .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new CandidateSummaryItem
                    {
                        Ticker = BuildSummaryTickerText(x)
                    })
                    .ToList(),
                TodayResearchLikeCandidates = []
            };
        }

        private string BuildSummaryTickerText(CandidateDetails candidate)
        {
            return
                $"{candidate.Ticker} " +
                $"{_fmt.Price(candidate.TradePlan.EntryPrice)} " +
                $"{_fmt.Price(candidate.TradePlan.ExitPrice)} " +
                $"{_fmt.Price(candidate.TradePlan.StopLoss)} " +
                $"{_fmt.Percent(candidate.TradePlan.ProfitPercent)}%/" +
                $"{_fmt.Percent(candidate.TradePlan.LossPercent)}%" +
                $" rank={_fmt.Generic(candidate.Score.NextDayRank ?? 0m)}" +
                $"{BuildSummaryMarkers(candidate)}";
        }

        private string BuildSummaryMarkers(CandidateDetails candidate)
        {
            var markers = new List<string>();

            if (candidate.NeedsDeeperEntry)
                markers.Add("deep-entry");

            if (candidate.NeedsMomentumExit)
                markers.Add("momentum-exit");

            if (IsParabolicExpansionProxy(candidate))
            {
                markers.Add("parabolic-expansion");
            }
            else if (IsDeepParabolicExpansionProxy(candidate))
            {
                markers.Add("deep-parabolic");
            }
            else if (IsExplosiveMinFirstProxy(candidate))
            {
                markers.Add("minfirst");
                markers.Add("explosive");
            }
            else if (IsConstructiveDeepMinFirstProxy(candidate))
            {
                markers.Add("minfirst");
                markers.Add("constructive-deep");
            }
            else if (IsStrongMinFirstProxy(candidate))
            {
                markers.Add("minfirst");
                markers.Add("strong");
            }
            else if (IsWeakDeepPullbackProxy(candidate))
            {
                markers.Add("minfirst");
                markers.Add("weak-deep");
            }

            return markers.Count == 0
                ? string.Empty
                : $" {string.Join(" ", markers)}";
        }

        private bool IsStrongMinFirstProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.StrongMinFirstExit;
            if (!settings.Enabled || candidate.NeedsDeeperEntry || candidate.NeedsMomentumExit)
                return false;

            return diagnostics.DailyTrendPosition >= settings.DailyTrendPositionThreshold &&
                   diagnostics.TrendPosition >= settings.TrendPositionThreshold &&
                   diagnostics.ATRRatio <= settings.MaxAtrRatio &&
                   context.DistanceTo20dHigh <= settings.MaxDistanceTo20dHigh;
        }

        private bool IsWeakDeepPullbackProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.WeakDeepPullbackExit;
            if (!settings.Enabled || !candidate.NeedsDeeperEntry)
                return false;

            var constructiveSettings = _getCandidatesSettingsProvider.Get().TradePlan.ConstructiveDeepMinFirst;
            var isConstructive =
                constructiveSettings.Enabled &&
                diagnostics.DailyTrendPosition >= constructiveSettings.MinDailyTrendPosition &&
                diagnostics.TrendPosition >= constructiveSettings.MinTrendPosition &&
                diagnostics.ATRRatio >= constructiveSettings.MinAtrRatio &&
                context.DailyRSI14 >= constructiveSettings.MinDailyRsi14;

            if (isConstructive)
                return false;

            return diagnostics.DailyTrendPosition <= settings.MaxDailyTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio;
        }

        private bool IsExplosiveMinFirstProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.ExplosiveMinFirstExit;
            if (!settings.Enabled || candidate.NeedsDeeperEntry || !candidate.NeedsMomentumExit)
                return false;

            if (IsParabolicExpansionProxy(candidate))
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   context.DailyRSI14 >= settings.MinDailyRsi14 &&
                   context.DistanceTo20dHigh <= settings.MaxDistanceTo20dHigh;
        }

        private bool IsConstructiveDeepMinFirstProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.ConstructiveDeepMinFirst;
            if (!settings.Enabled || !candidate.NeedsDeeperEntry)
                return false;

            if (IsDeepParabolicExpansionProxy(candidate))
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   context.DailyRSI14 >= settings.MinDailyRsi14;
        }

        private bool IsParabolicExpansionProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.ParabolicExpansionExit;
            if (!settings.Enabled || candidate.NeedsDeeperEntry || !candidate.NeedsMomentumExit)
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   context.DailyRSI14 >= settings.MinDailyRsi14 &&
                   context.DistanceTo20dHigh >= settings.MaxDistanceTo20dHigh &&
                   diagnostics.VolumeRatio20 >= settings.MinVolumeRatio20;
        }

        private bool IsDeepParabolicExpansionProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.DeepParabolicExpansionExit;
            if (!settings.Enabled || !candidate.NeedsDeeperEntry)
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   context.DailyRSI14 >= settings.MinDailyRsi14 &&
                   diagnostics.VolumeRatio20 >= settings.MinVolumeRatio20;
        }

        private static bool AreSummariesEqual(
            CandidateSummarySections? left,
            CandidateSummarySections? right)
        {
            left ??= new CandidateSummarySections();
            right ??= new CandidateSummarySections();

            return AreSummaryListsEqual(left.ReversalCandidates, right.ReversalCandidates) &&
                   AreSummaryListsEqual(left.TodayResearchLikeCandidates, right.TodayResearchLikeCandidates);
        }

        private static bool AreSummaryListsEqual(
            List<CandidateSummaryItem>? left,
            List<CandidateSummaryItem>? right)
        {
            left ??= [];
            right ??= [];

            if (left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i].Ticker, right[i].Ticker, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private async Task<int> CleanWishListAsync(CleanUpSettings settings, DateTime marketNow)
        {
            if (!settings.RemoveStaleWishListItems)
                return 0;

            var wishListPath = _pathService.GetWishListFile();
            var items = await _jsonFileService.ReadAsync<List<WishListItem>>(wishListPath) ?? [];
            if (items.Count == 0)
                return 0;

            var removedReasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var kept = new List<WishListItem>();

            foreach (var item in items)
            {
                var reason = GetWishListRemovalReason(item, settings, marketNow);

                if (reason == null)
                {
                    kept.Add(item);
                    continue;
                }

                if (!removedReasons.TryAdd(reason, 1))
                    removedReasons[reason]++;
            }

            var removed = items.Count - kept.Count;
            if (removed <= 0)
                return 0;

            await _jsonFileService.WriteAsync(wishListPath, kept);

            var reasonsText = string.Join(
                ", ",
                removedReasons
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => $"{x.Key}={x.Value}"));

            _logger.Info(
                $"Wish list cleaned: removed={removed}, kept={kept.Count}, reasons: {reasonsText}");

            return removed;
        }

        private int CleanOldFiles(CleanUpSettings settings, DateTime marketNow)
        {
            if (settings.FileRetentionDays <= 0)
                return 0;

            var deleted = 0;
            var cutoff = marketNow.Date.AddDays(-settings.FileRetentionDays);

            if (settings.DeleteLogsOlderThanCutoff)
                deleted += DeleteFilesOlderThan(_pathService.GetLogsFolder(), cutoff, "logs");

            var dataRoot = _pathService.GetDataRoot();

            if (settings.DeleteLegacyCandidatesOlderThanCutoff)
                deleted += DeleteFilesOlderThan(Path.Combine(dataRoot, "candidates"), cutoff, "legacy candidates");

            if (settings.DeleteLegacyEvaluationsOlderThanCutoff)
                deleted += DeleteFilesOlderThan(Path.Combine(dataRoot, "evaluations"), cutoff, "legacy evaluations");

            return deleted;
        }

        private int DeleteFilesOlderThan(string folderPath, DateTime cutoff, string label)
        {
            if (!Directory.Exists(folderPath))
                return 0;

            var deleted = 0;

            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                var lastWriteTime = File.GetLastWriteTime(file);
                if (lastWriteTime >= cutoff)
                    continue;

                File.Delete(file);
                deleted++;
            }

            if (deleted > 0)
                _logger.Info($"Old {label} files deleted: {deleted} (cutoff={cutoff:yyyy-MM-dd})");

            return deleted;
        }

        private static string? GetWishListRemovalReason(
            WishListItem item,
            CleanUpSettings settings,
            DateTime marketNow)
        {
            if (string.Equals(item.LastStatus, "Remove", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(item.LastStatusReason)
                    ? "Marked for removal by wish list evaluation"
                    : $"Marked for removal: {item.LastStatusReason}";

            var firstSeen = item.FirstSeen ?? item.Scan.ScanTime;
            var ageDays = (marketNow.Date - firstSeen.Date).TotalDays;

            if (!item.ExpectedTargetTime.HasValue &&
                settings.RemoveWishListWithoutTargetOlderThanDays > 0 &&
                ageDays >= settings.RemoveWishListWithoutTargetOlderThanDays)
            {
                return $"No target for {settings.RemoveWishListWithoutTargetOlderThanDays}+ days";
            }

            if (item.ExpectedTargetTime.HasValue &&
                settings.RemoveWishListPastExpectedTargetGraceDays > 0 &&
                marketNow.Date > item.ExpectedTargetTime.Value.Date.AddDays(settings.RemoveWishListPastExpectedTargetGraceDays))
            {
                return $"Past expected target by {settings.RemoveWishListPastExpectedTargetGraceDays}+ days";
            }

            return null;
        }

        private static async Task<HashSet<string>> LoadCandidateKeysByOutcomeAsync(
            string evaluationsPath,
            HashSet<string> outcomes)
        {
            var lines = await File.ReadAllLinesAsync(evaluationsPath);
            if (lines.Length <= 1)
                return [];

            var delimiter = DetectDelimiter(lines[0]);
            var headers = SplitCsvLine(lines[0], delimiter);
            var tickerIndex = FindHeaderIndex(headers, "Ticker");
            var scanTimeIndex = FindHeaderIndex(headers, "ScanTime");
            if (scanTimeIndex < 0)
                scanTimeIndex = FindHeaderIndex(headers, "ScanTimeMarket");
            if (scanTimeIndex < 0)
                scanTimeIndex = FindHeaderIndex(headers, "ScanTimeNy");
            var presetIndex = FindHeaderIndex(headers, "PresetScanCode");
            var outcomeIndex = FindHeaderIndex(headers, "Outcome");

            if (tickerIndex < 0 || scanTimeIndex < 0 || presetIndex < 0 || outcomeIndex < 0)
                return [];

            var latestByCandidate = new Dictionary<string, (string Outcome, DateTime EvaluatedAt)>(
                StringComparer.OrdinalIgnoreCase);

            var evaluatedAtIndex = FindHeaderIndex(headers, "EvaluatedAt");
            if (evaluatedAtIndex < 0)
                evaluatedAtIndex = FindHeaderIndex(headers, "EvaluatedAtMarketTime");
            if (evaluatedAtIndex < 0)
                evaluatedAtIndex = FindHeaderIndex(headers, "EvaluationEndTime");

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = SplitCsvLine(line, delimiter);
                if (parts.Count <= Math.Max(Math.Max(tickerIndex, scanTimeIndex), Math.Max(presetIndex, outcomeIndex)))
                    continue;

                if (!DateTime.TryParse(parts[scanTimeIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var scanTime))
                    continue;

                var evaluatedAt = scanTime;
                if (evaluatedAtIndex >= 0 &&
                    evaluatedAtIndex < parts.Count &&
                    DateTime.TryParse(parts[evaluatedAtIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedEvaluatedAt))
                {
                    evaluatedAt = parsedEvaluatedAt;
                }

                var candidateKey = BuildCandidateKey(parts[tickerIndex], parts[presetIndex], scanTime);
                var outcome = parts[outcomeIndex].Trim();

                if (!latestByCandidate.TryGetValue(candidateKey, out var existing) ||
                    evaluatedAt > existing.EvaluatedAt)
                {
                    latestByCandidate[candidateKey] = (outcome, evaluatedAt);
                }
            }

            return latestByCandidate
                .Where(x => outcomes.Contains(x.Value.Outcome))
                .Select(x => x.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static int FindHeaderIndex(List<string> headers, string headerName)
        {
            return headers.FindIndex(
                x => string.Equals(x, headerName, StringComparison.OrdinalIgnoreCase));
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
            var sb = new System.Text.StringBuilder();
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

        private static string BuildCandidateKey(string ticker, string presetScanCode, DateTime scanTimeMarket)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{ticker}|{presetScanCode}|{scanTimeMarket:yyyy-MM-dd HH:mm:ss}");
        }
    }
}
