namespace IbSwingTrader.App.Commands
{
    public class EvaluateCandidatesCommand(
        ITwsConnection twsConnection,
        ICandidateEvaluator candidateEvaluator,
        IJsonFileService jsonFileService,
        ICandidateEvaluationCsvService candidateCsvService,
        ITextLogger logger,
        IAgentPathService pathService,
        ITwsSettingsProvider twsSettingsProvider) : ICommand
    {
        private readonly ITwsConnection _twsConnection = twsConnection;
        private readonly ICandidateEvaluator _candidateEvaluator = candidateEvaluator;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly ICandidateEvaluationCsvService _candidateCsvService = candidateCsvService;
        private readonly ITextLogger _logger = logger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITwsSettingsProvider _twsSettingsProvider = twsSettingsProvider;

        public async Task RunAsync()
        {
            var twsSettings = _twsSettingsProvider.Get();

            var candidatesPath = _pathService.GetCandidatesFile();
            var evaluationsPath = _pathService.GetEvaluationsFile();

            EnsureConnected(twsSettings.ConnectTimeoutSeconds);

            var evaluationsFolder = Path.GetDirectoryName(evaluationsPath);
            if (!string.IsNullOrWhiteSpace(evaluationsFolder))
                Directory.CreateDirectory(evaluationsFolder);

            await EvaluateCandidatesAsync(candidatesPath, evaluationsPath);

            _logger.Info("Candidate evaluation completed.");
        }

        private async Task EvaluateCandidatesAsync(
            string candidatesPath,
            string evaluationsPath)
        {
            var candidates = await LoadCandidatesAsync(candidatesPath);
            _logger.Info($"Candidates found: {candidates.Count}");

            if (candidates.Count == 0)
                return;

            var evaluationKeys = await LoadEvaluationKeysAsync(evaluationsPath);
            var pending = candidates
                .Where(x => !evaluationKeys.Contains(BuildCandidateKey(x)))
                .ToList();

            _logger.Info($"Pending candidates for evaluation: {pending.Count}");

            if (pending.Count == 0)
                return;

            var results = await _candidateEvaluator.EvaluateAsync(pending);
            await _candidateCsvService.WriteAsync(evaluationsPath, results);
            LogCandidateSummary(Path.GetFileName(candidatesPath), results);
        }

        private void EnsureConnected(int timeoutSeconds)
        {
            if (_twsConnection.IsConnected)
                return;

            _logger.Info("Connecting to TWS...");

            _twsConnection.Connect();

            var connected = _twsConnection.Ready.Task
                .Wait(TimeSpan.FromSeconds(timeoutSeconds));

            if (!connected || !_twsConnection.IsConnected)
                throw new InvalidOperationException("Failed to connect to TWS.");

            _logger.Info("TWS connected.");
        }

        private void LogCandidateSummary(
            string sourceFileName,
            List<CandidateEvaluationResult> results)
        {
            var wins = results.Count(x => x.Outcome == "Win");
            var losses = results.Count(x => x.Outcome == "Loss");
            var open = results.Count(x => x.Outcome == "Open");
            var noEntry = results.Count(x => x.Outcome == "NoEntry");
            var noData = results.Count(x =>
                x.Outcome == "NoData" ||
                x.Outcome == "NoDataAfterScan");
            var errors = results.Count(x =>
                x.Outcome != null &&
                x.Outcome.StartsWith("Error:", StringComparison.OrdinalIgnoreCase));

            _logger.Info(
                $"Done {sourceFileName} | Total={results.Count} Win={wins} Loss={losses} Open={open} NoEntry={noEntry} NoData={noData} Errors={errors}");
        }

        private async Task<List<CandidateDetails>> LoadCandidatesAsync(string candidatesPath)
        {
            if (File.Exists(candidatesPath))
                return await _jsonFileService.ReadAsync<List<CandidateDetails>>(candidatesPath) ?? [];

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

            return result
                .GroupBy(BuildCandidateKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.OrderByDescending(y => y.Scan.ScanTimeMarket).First())
                .ToList();
        }

        private async Task<HashSet<string>> LoadEvaluationKeysAsync(string evaluationsPath)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(evaluationsPath))
                await AddEvaluationKeysFromCsvAsync(evaluationsPath, result);

            var legacyFolder = _pathService.GetLegacyEvaluationsFolder();
            if (Directory.Exists(legacyFolder))
            {
                foreach (var legacyFile in Directory
                             .GetFiles(legacyFolder, "*.csv", SearchOption.TopDirectoryOnly)
                             .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    await AddEvaluationKeysFromCsvAsync(legacyFile, result);
                }
            }

            return result;
        }

        private static async Task AddEvaluationKeysFromCsvAsync(
            string csvPath,
            HashSet<string> target)
        {
            var lines = await File.ReadAllLinesAsync(csvPath);
            if (lines.Length <= 1)
                return;

            var headers = lines[0].Split(';');
            var tickerIndex = Array.FindIndex(headers, x => string.Equals(x, "Ticker", StringComparison.OrdinalIgnoreCase));
            var scanTimeIndex = Array.FindIndex(headers, x => string.Equals(x, "ScanTimeNy", StringComparison.OrdinalIgnoreCase));
            var presetIndex = Array.FindIndex(headers, x => string.Equals(x, "PresetScanCode", StringComparison.OrdinalIgnoreCase));

            if (tickerIndex < 0 || scanTimeIndex < 0 || presetIndex < 0)
                return;

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(';');
                if (parts.Length <= Math.Max(tickerIndex, Math.Max(scanTimeIndex, presetIndex)))
                    continue;

                target.Add($"{parts[tickerIndex]}|{parts[presetIndex]}|{parts[scanTimeIndex]}");
            }
        }

        private static string BuildCandidateKey(CandidateDetails candidate)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{candidate.Ticker}|{candidate.Scan.PresetScanCode}|{candidate.Scan.ScanTimeMarket:O}");
        }
    }
}
