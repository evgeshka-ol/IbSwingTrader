namespace IbSwingTrader.App.Commands
{
    public class EvaluateCandidatesCommand(
        ITwsConnection twsConnection,
        ICandidateEvaluator candidateEvaluator,
        IJsonFileService jsonFileService,
        ICandidateEvaluationCsvService candidateCsvService,
        ITextLogger logger,
        IAgentPathService pathService,
        ITwsSettingsProvider twsSettingsProvider,
        ICandidateEvaluationSettingsProvider candidateEvaluationSettingsProvider) : ICommand
    {
        private readonly ITwsConnection _twsConnection = twsConnection;
        private readonly ICandidateEvaluator _candidateEvaluator = candidateEvaluator;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly ICandidateEvaluationCsvService _candidateCsvService = candidateCsvService;
        private readonly ITextLogger _logger = logger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITwsSettingsProvider _twsSettingsProvider = twsSettingsProvider;
        private readonly ICandidateEvaluationSettingsProvider _candidateEvaluationSettingsProvider = candidateEvaluationSettingsProvider;

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

            if (!File.Exists(evaluationsPath))
                await _candidateCsvService.WriteAsync(evaluationsPath, []);

            var evaluationState = await LoadEvaluationStateAsync(evaluationsPath);
            var pending = candidates
                .Where(x => ShouldEvaluate(x, evaluationState))
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

        private async Task<Dictionary<string, CandidateEvaluationState>> LoadEvaluationStateAsync(string evaluationsPath)
        {
            var result = new Dictionary<string, CandidateEvaluationState>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(evaluationsPath))
                await AddEvaluationStateFromCsvAsync(evaluationsPath, result);

            return result;
        }

        private static async Task AddEvaluationStateFromCsvAsync(
            string csvPath,
            Dictionary<string, CandidateEvaluationState> target)
        {
            var lines = await File.ReadAllLinesAsync(csvPath);
            if (lines.Length <= 1)
                return;

            var headers = lines[0].Split(';');
            var tickerIndex = Array.FindIndex(headers, x => string.Equals(x, "Ticker", StringComparison.OrdinalIgnoreCase));
            var scanTimeIndex = Array.FindIndex(headers, x => string.Equals(x, "ScanTimeNy", StringComparison.OrdinalIgnoreCase));
            var presetIndex = Array.FindIndex(headers, x => string.Equals(x, "PresetScanCode", StringComparison.OrdinalIgnoreCase));
            var outcomeIndex = Array.FindIndex(headers, x => string.Equals(x, "Outcome", StringComparison.OrdinalIgnoreCase));
            var evaluationEndTimeIndex = Array.FindIndex(headers, x => string.Equals(x, "EvaluationEndTime", StringComparison.OrdinalIgnoreCase));

            if (tickerIndex < 0 || scanTimeIndex < 0 || presetIndex < 0 || outcomeIndex < 0 || evaluationEndTimeIndex < 0)
                return;

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(';');
                if (parts.Length <= Math.Max(evaluationEndTimeIndex, Math.Max(outcomeIndex, Math.Max(tickerIndex, Math.Max(scanTimeIndex, presetIndex)))))
                    continue;

                var key = $"{parts[tickerIndex]}|{parts[presetIndex]}|{parts[scanTimeIndex]}";
                var outcome = parts[outcomeIndex];
                var evaluationEndTime = TryParseDateTime(parts[evaluationEndTimeIndex]);

                target[key] = new CandidateEvaluationState
                {
                    Outcome = outcome,
                    EvaluationEndTime = evaluationEndTime
                };
            }
        }

        private bool ShouldEvaluate(
            CandidateDetails candidate,
            Dictionary<string, CandidateEvaluationState> evaluationState)
        {
            var key = BuildCandidateKey(candidate);

            if (!evaluationState.TryGetValue(key, out var state))
                return true;

            return !IsFinalOutcome(candidate, state);
        }

        private bool IsFinalOutcome(
            CandidateDetails candidate,
            CandidateEvaluationState state)
        {
            if (string.IsNullOrWhiteSpace(state.Outcome))
                return false;

            if (state.Outcome.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (state.Outcome is "Win" or "Loss")
                return true;

            var settings = _candidateEvaluationSettingsProvider.Get();
            var requestedEnd = candidate.Scan.ScanTimeMarket.AddDays(settings.ForwardEvaluationDays);

            if (!state.EvaluationEndTime.HasValue)
                return false;

            var reachedFullWindow = state.EvaluationEndTime.Value >= requestedEnd;
            if (!reachedFullWindow)
                return false;

            return state.Outcome is "Open" or "NoEntry" or "NoData" or "NoDataAfterScan" or "InsufficientFutureData";
        }

        private static string BuildCandidateKey(CandidateDetails candidate)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{candidate.Ticker}|{candidate.Scan.PresetScanCode}|{candidate.Scan.ScanTimeMarket:O}");
        }

        private static DateTime? TryParseDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParse(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private sealed class CandidateEvaluationState
        {
            public string Outcome { get; set; } = string.Empty;
            public DateTime? EvaluationEndTime { get; set; }
        }
    }
}
