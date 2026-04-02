using System.Globalization;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.App.Commands
{
    public class CleanUpCommand(
        IJsonFileService jsonFileService,
        ITextLogger logger,
        IAgentPathService pathService,
        ICleanUpSettingsProvider cleanUpSettingsProvider,
        IMarketSettingsProvider marketSettingsProvider) : ICommand
    {
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly ITextLogger _logger = logger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ICleanUpSettingsProvider _cleanUpSettingsProvider = cleanUpSettingsProvider;
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;

        public async Task RunAsync()
        {
            var settings = _cleanUpSettingsProvider.Get();
            var marketNow = MarketTime.Now(_marketSettingsProvider.Get().Timezone);

            var candidatesRemoved = await CleanCandidatesAsync(settings);
            var wishListRemoved = await CleanWishListAsync(settings, marketNow);
            var deletedFiles = CleanOldFiles(settings, marketNow);

            _logger.Info(
                $"Clean-up completed. CandidatesRemoved={candidatesRemoved} WishListRemoved={wishListRemoved} FilesDeleted={deletedFiles}");
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
                .Where(x => !evaluatedKeys.Contains(BuildCandidateKey(x.Ticker, x.Scan.PresetScanCode, x.Scan.ScanTimeMarket)))
                .ToList();

            var removed = candidates.Count - filtered.Count;
            if (removed <= 0)
                return 0;

            candidateDocument.Candidates = filtered;
            candidateDocument.Summary = BuildSummary(filtered);
            await _jsonFileService.WriteAsync(candidatesPath, candidateDocument);

            _logger.Info(
                $"Candidates cleaned: removed={removed}, kept={filtered.Count}, outcomes=[{string.Join(", ", removableOutcomes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}]");

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

            return await _jsonFileService.ReadAsync<CandidateFileDocument>(candidatesPath)
                ?? new CandidateFileDocument();
        }

        private static List<CandidateSummaryItem> BuildSummary(List<CandidateDetails> candidates)
        {
            if (candidates.Count == 0)
                return [];

            var latestScanTime = candidates.Max(x => x.Scan.ScanTimeMarket);

            return candidates
                .Where(x => x.Scan.ScanTimeMarket == latestScanTime)
                .OrderByDescending(x => x.Score.Score)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .Select(x => new CandidateSummaryItem
                {
                    Ticker =
                        $"{x.Ticker} " +
                        $"{x.TradePlan.EntryPrice:0.##} " +
                        $"{x.TradePlan.ExitPrice:0.##} " +
                        $"{x.TradePlan.StopLoss:0.##} " +
                        $"{x.TradePlan.ProfitPercent:0.##}%/" +
                        $"{x.TradePlan.LossPercent:0.##}%"
                })
                .ToList();
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

            var firstSeen = item.FirstSeenMarketTime ?? item.Scan.ScanTimeMarket;
            var ageDays = (marketNow.Date - firstSeen.Date).TotalDays;

            if (!item.ExpectedTargetMarketTime.HasValue &&
                settings.RemoveWishListWithoutTargetOlderThanDays > 0 &&
                ageDays >= settings.RemoveWishListWithoutTargetOlderThanDays)
            {
                return $"No target for {settings.RemoveWishListWithoutTargetOlderThanDays}+ days";
            }

            if (item.ExpectedTargetMarketTime.HasValue &&
                settings.RemoveWishListPastExpectedTargetGraceDays > 0 &&
                marketNow.Date > item.ExpectedTargetMarketTime.Value.Date.AddDays(settings.RemoveWishListPastExpectedTargetGraceDays))
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

            var headers = lines[0].Split(';');
            var tickerIndex = FindHeaderIndex(headers, "Ticker");
            var scanTimeIndex = FindHeaderIndex(headers, "ScanTimeMarket");
            if (scanTimeIndex < 0)
                scanTimeIndex = FindHeaderIndex(headers, "ScanTimeNy");
            var presetIndex = FindHeaderIndex(headers, "PresetScanCode");
            var outcomeIndex = FindHeaderIndex(headers, "Outcome");

            if (tickerIndex < 0 || scanTimeIndex < 0 || presetIndex < 0 || outcomeIndex < 0)
                return [];

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(';');
                if (parts.Length <= Math.Max(Math.Max(tickerIndex, scanTimeIndex), Math.Max(presetIndex, outcomeIndex)))
                    continue;

                var outcome = parts[outcomeIndex].Trim();
                if (!outcomes.Contains(outcome))
                    continue;

                if (!DateTime.TryParse(parts[scanTimeIndex], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var scanTime))
                    continue;

                result.Add(BuildCandidateKey(parts[tickerIndex], parts[presetIndex], scanTime));
            }

            return result;
        }

        private static int FindHeaderIndex(string[] headers, string headerName)
        {
            return Array.FindIndex(
                headers,
                x => string.Equals(x, headerName, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildCandidateKey(string ticker, string presetScanCode, DateTime scanTimeMarket)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{ticker}|{presetScanCode}|{scanTimeMarket:yyyy-MM-dd HH:mm:ss}");
        }
    }
}
