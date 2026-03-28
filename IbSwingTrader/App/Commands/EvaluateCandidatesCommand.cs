namespace IbSwingTrader.App.Commands
{
    public class EvaluateCandidatesCommand(
        ITwsConnection twsConnection,
        ICandidateEvaluator candidateEvaluator,
        IJsonFileService jsonFileService,
        ICandidateEvaluationCsvService candidateCsvService,
        IProcessedCandidateFilesService processedFilesService,
        IFileHashService fileHashService,
        ITextLogger logger,
        IAgentPathService pathService,
        ICandidateEvaluationSettingsProvider evaluationSettingsProvider,
        ITwsSettingsProvider twsSettingsProvider) : ICommand
    {
        private readonly ITwsConnection _twsConnection = twsConnection;
        private readonly ICandidateEvaluator _candidateEvaluator = candidateEvaluator;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly ICandidateEvaluationCsvService _candidateCsvService = candidateCsvService;
        private readonly IProcessedCandidateFilesService _processedFilesService = processedFilesService;
        private readonly IFileHashService _fileHashService = fileHashService;
        private readonly ITextLogger _logger = logger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ICandidateEvaluationSettingsProvider _evaluationSettingsProvider = evaluationSettingsProvider;
        private readonly ITwsSettingsProvider _twsSettingsProvider = twsSettingsProvider;

        public async Task RunAsync()
        {
            var evaluationSettings = _evaluationSettingsProvider.Get();
            var twsSettings = _twsSettingsProvider.Get();

            var candidatesFolder = _pathService.GetCandidatesFolder();
            var evaluationsFolder = _pathService.GetEvaluationsFolder();
            var manifestPath = _pathService.GetProcessedCandidateFilesManifest();

            EnsureConnected(twsSettings.ConnectTimeoutSeconds);

            Directory.CreateDirectory(candidatesFolder);
            Directory.CreateDirectory(evaluationsFolder);

            await EvaluateCandidateFilesAsync(
                candidatesFolder,
                evaluationsFolder,
                manifestPath,
                evaluationSettings.SearchPattern);

            _logger.Info("Candidate evaluation completed.");
        }

        private async Task EvaluateCandidateFilesAsync(
            string candidatesFolder,
            string evaluationsFolder,
            string manifestPath,
            string searchPattern)
        {
            var manifest = await _processedFilesService.ReadAsync(manifestPath);

            var files = Directory
                .GetFiles(
                    candidatesFolder,
                    searchPattern,
                    SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.Info($"Candidate files found: {files.Count}");

            var manifestChanged = false;

            foreach (var file in files)
            {
                var processed = await ProcessCandidateFileAsync(
                    file,
                    evaluationsFolder,
                    manifestPath,
                    manifest);

                manifestChanged = manifestChanged || processed;
            }

            if (manifestChanged)
                await _processedFilesService.WriteAsync(manifestPath, manifest);
        }

        private async Task<bool> ProcessCandidateFileAsync(
            string filePath,
            string evaluationsFolder,
            string manifestPath,
            ProcessedCandidateFilesManifest manifest)
        {
            var fileInfo = new FileInfo(filePath);
            var sha256 = await _fileHashService.ComputeSha256Async(filePath);

            if (_processedFilesService.IsProcessed(manifest, sha256))
            {
                _logger.Info($"Skipping already processed file: {fileInfo.Name}");
                return false;
            }

            _logger.Info($"Processing candidate file: {fileInfo.Name}");

            var candidates = await _jsonFileService.ReadAsync<List<CandidateDetails>>(filePath);

            if (candidates == null || candidates.Count == 0)
            {
                _logger.Warning($"No candidates in file: {fileInfo.Name}");
                return false;
            }

            var results = await _candidateEvaluator.EvaluateAsync(candidates);

            var outputFileName = BuildCandidateOutputCsvFileName(fileInfo.Name);
            var outputFullPath = Path.Combine(evaluationsFolder, outputFileName);

            await _candidateCsvService.WriteAsync(outputFullPath, results);

            _processedFilesService.MarkProcessed(
                manifest,
                new ProcessedCandidateFile
                {
                    FileName = fileInfo.Name,
                    FullPath = fileInfo.FullName,
                    FileSize = fileInfo.Length,
                    LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                    Sha256 = sha256,
                    ProcessedAtUtc = DateTime.UtcNow,
                    CandidateCount = candidates.Count,
                    EvaluationCount = results.Count,
                    Status = "Completed",
                    OutputCsvFileName = outputFileName,
                    OutputCsvFullPath = outputFullPath
                });

            await _processedFilesService.WriteAsync(manifestPath, manifest);

            LogCandidateSummary(fileInfo.Name, results);

            return true;
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

        private static string BuildCandidateOutputCsvFileName(string inputFileName)
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(inputFileName);
            return $"evaluation_{nameWithoutExtension}.csv";
        }
    }
}
