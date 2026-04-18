namespace IbSwingTrader.App.Commands
{
    public class EvaluateCandidatesCommand(
        ITwsConnection twsConnection,
        ICandidateEvaluator candidateEvaluator,
        BuildEvaluationDatasetCommand buildEvaluationDatasetCommand,
        IJsonFileService jsonFileService,
        ICandidateEvaluationCsvService candidateCsvService,
        ITextLogger logger,
        IAgentPathService pathService,
        ITwsSettingsProvider twsSettingsProvider) : ICommand
    {
        private readonly ITwsConnection _twsConnection = twsConnection;
        private readonly ICandidateEvaluator _candidateEvaluator = candidateEvaluator;
        private readonly BuildEvaluationDatasetCommand _buildEvaluationDatasetCommand = buildEvaluationDatasetCommand;
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

            _logger.Info("Rebuilding evaluation dataset after candidate evaluation...");
            await _buildEvaluationDatasetCommand.RunAsync();

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

            if (!File.Exists(evaluationsPath))
                await _candidateCsvService.WriteAsync(evaluationsPath, []);

            var existingEvaluations = await _candidateCsvService.ReadAsync(evaluationsPath);
            var canonicalByScanKey = existingEvaluations
                .GroupBy(BuildScanKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x
                        .OrderBy(y => y.EvaluatedAtMarketTime)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var pending = new List<CandidateDetails>();

            foreach (var candidate in candidates)
            {
                var scanKey = BuildScanKey(candidate);

                if (canonicalByScanKey.TryGetValue(scanKey, out var canonical))
                    ApplyCanonicalSnapshot(candidate, canonical);

                pending.Add(candidate);
            }

            _logger.Info($"Pending candidates for evaluation: {pending.Count}");

            if (pending.Count == 0)
                return;

            var results = await _candidateEvaluator.EvaluateAsync(pending);

            foreach (var result in results)
            {
                if (canonicalByScanKey.TryGetValue(BuildScanKey(result), out var canonical))
                {
                    result.StrategyVersion = canonical.StrategyVersion;
                    result.CandidateScore = canonical.CandidateScore;
                }
            }

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
            if (!File.Exists(candidatesPath))
                return [];

            var json = await File.ReadAllTextAsync(candidatesPath);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            var firstNonWhitespace = json.FirstOrDefault(x => !char.IsWhiteSpace(x));

            if (firstNonWhitespace == '[')
                return await _jsonFileService.ReadAsync<List<CandidateDetails>>(candidatesPath) ?? [];

            var document = await _jsonFileService.ReadAsync<CandidateFileDocument>(candidatesPath);
            return document?.Candidates ?? [];
        }

        private static void ApplyCanonicalSnapshot(
            CandidateDetails candidate,
            CandidateEvaluationResult canonical)
        {
            candidate.TradePlan.EntryPrice = canonical.EntryPrice;
            candidate.TradePlan.ExitPrice = canonical.ExitPrice;
            candidate.TradePlan.StopLoss = canonical.StopLoss;
            candidate.TradePlan.ProfitPercent = CalcPct(canonical.EntryPrice, canonical.ExitPrice);
            candidate.TradePlan.LossPercent = CalcPct(canonical.EntryPrice, canonical.StopLoss);
            candidate.Score.Score = canonical.CandidateScore;
        }

        private static string BuildScanKey(CandidateDetails candidate)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{candidate.Ticker}|{candidate.Scan.PresetScanCode}|{candidate.Scan.ScanTimeMarket:O}");
        }

        private static string BuildScanKey(CandidateEvaluationResult result)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{result.Ticker}|{result.PresetScanCode}|{result.ScanTimeMarket:O}");
        }

        private static decimal CalcPct(decimal from, decimal to)
        {
            if (from == 0m)
                return 0m;

            return (to - from) / from * 100m;
        }

    }
}
