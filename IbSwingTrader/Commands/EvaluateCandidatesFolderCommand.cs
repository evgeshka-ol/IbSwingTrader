using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Commands
{
    public class EvaluateCandidatesFolderCommand : ICommand
    {
        private readonly ITwsConnection _twsConnection;
        private readonly ICandidateEvaluator _candidateEvaluator;
        private readonly IJsonFileService _jsonFileService;
        private readonly ICandidateEvaluationCsvService _csvService;
        private readonly IProcessedCandidateFilesService _processedFilesService;
        private readonly IFileHashService _fileHashService;
        private readonly ITextLogger _logger;

        private readonly string _candidatesFolder;
        private readonly string _evaluationsFolder;
        private readonly string _manifestPath;
        private readonly string _searchPattern;

        public EvaluateCandidatesFolderCommand(
            ITwsConnection twsConnection,
            ICandidateEvaluator candidateEvaluator,
            IJsonFileService jsonFileService,
            ICandidateEvaluationCsvService csvService,
            IProcessedCandidateFilesService processedFilesService,
            IFileHashService fileHashService,
            ITextLogger logger,
            string candidatesFolder,
            string evaluationsFolder,
            string manifestPath,
            string searchPattern = "candidates_*.json")
        {
            _twsConnection = twsConnection;
            _candidateEvaluator = candidateEvaluator;
            _jsonFileService = jsonFileService;
            _csvService = csvService;
            _processedFilesService = processedFilesService;
            _fileHashService = fileHashService;
            _logger = logger;
            _candidatesFolder = candidatesFolder;
            _evaluationsFolder = evaluationsFolder;
            _manifestPath = manifestPath;
            _searchPattern = searchPattern;
        }

        public async Task RunAsync()
        {
            EnsureConnected();

            Directory.CreateDirectory(_candidatesFolder);
            Directory.CreateDirectory(_evaluationsFolder);

            var manifest = await _processedFilesService.ReadAsync(_manifestPath);

            var files = Directory
                .GetFiles(_candidatesFolder, _searchPattern, SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.Info($"Candidate files found: {files.Count}");

            var manifestChanged = false;

            foreach (var file in files)
            {
                var processed = await ProcessFileAsync(file, manifest);
                manifestChanged = manifestChanged || processed;
            }

            if (manifestChanged)
                await _processedFilesService.WriteAsync(_manifestPath, manifest);

            _logger.Info("Folder evaluation completed.");
        }

        private void EnsureConnected()
        {
            if (_twsConnection.IsConnected)
                return;

            _logger.Info("Connecting to TWS...");

            _twsConnection.Connect();

            var connected = _twsConnection.Ready.Task
                .Wait(TimeSpan.FromSeconds(15));

            if (!connected || !_twsConnection.IsConnected)
                throw new InvalidOperationException("Failed to connect to TWS.");

            _logger.Info("TWS connected.");
        }

        private async Task<bool> ProcessFileAsync(
            string filePath,
            ProcessedCandidateFilesManifest manifest)
        {
            var fileInfo = new FileInfo(filePath);
            var sha256 = await _fileHashService.ComputeSha256Async(filePath);

            if (_processedFilesService.IsProcessed(manifest, sha256))
            {
                _logger.Info($"Skipping already processed file: {fileInfo.Name}");
                return false;
            }

            _logger.Info($"Processing file: {fileInfo.Name}");

            var candidates = await _jsonFileService.ReadAsync<List<CandidateDetails>>(filePath);
            if (candidates == null || candidates.Count == 0)
            {
                _logger.Warning($"No candidates in file: {fileInfo.Name}");
                return false;
            }

            var results = await _candidateEvaluator.EvaluateAsync(candidates);

            var outputFileName = BuildOutputCsvFileName(fileInfo.Name);
            var outputFullPath = Path.Combine(_evaluationsFolder, outputFileName);

            await _csvService.WriteAsync(outputFullPath, results);

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

            await _processedFilesService.WriteAsync(_manifestPath, manifest);

            LogSummary(fileInfo.Name, results);

            return true;
        }

        private void LogSummary(
            string sourceFileName,
            List<CandidateEvaluationResult> results)
        {
            var wins = results.Count(x =>
                string.Equals(x.Outcome, "Win", StringComparison.OrdinalIgnoreCase));

            var losses = results.Count(x =>
                string.Equals(x.Outcome, "Loss", StringComparison.OrdinalIgnoreCase));

            var open = results.Count(x =>
                string.Equals(x.Outcome, "Open", StringComparison.OrdinalIgnoreCase));

            var noEntry = results.Count(x =>
                string.Equals(x.Outcome, "NoEntry", StringComparison.OrdinalIgnoreCase));

            var noData = results.Count(x =>
                string.Equals(x.Outcome, "NoData", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Outcome, "NoDataAfterScan", StringComparison.OrdinalIgnoreCase));

            var insufficientFutureData = results.Count(x =>
                string.Equals(x.Outcome, "InsufficientFutureData", StringComparison.OrdinalIgnoreCase));

            var errors = results.Count(x =>
                !string.IsNullOrWhiteSpace(x.Outcome) &&
                x.Outcome.StartsWith("Error:", StringComparison.OrdinalIgnoreCase));

            var hitPlus5BeforeMinus5 = results.Count(x => x.HitPlus5BeforeMinus5 == true);
            var hitPlus7BeforeMinus5 = results.Count(x => x.HitPlus7BeforeMinus5 == true);
            var hitPlus10BeforeMinus5 = results.Count(x => x.HitPlus10BeforeMinus5 == true);

            var target3Pct1DHit = results.Count(x => x.Target3Pct1DHit);
            var target5Pct1DHit = results.Count(x => x.Target5Pct1DHit);
            var target7Pct1DHit = results.Count(x => x.Target7Pct1DHit);
            var target10Pct1DHit = results.Count(x => x.Target10Pct1DHit);
            var target15Pct1DHit = results.Count(x => x.Target15Pct1DHit);

            _logger.Info(
                $"Done {sourceFileName} | " +
                $"Total={results.Count} " +
                $"Win={wins} " +
                $"Loss={losses} " +
                $"Open={open} " +
                $"NoEntry={noEntry} " +
                $"NoData={noData} " +
                $"InsufficientFutureData={insufficientFutureData} " +
                $"Hit+5Before-5={hitPlus5BeforeMinus5} " +
                $"Hit+7Before-5={hitPlus7BeforeMinus5} " +
                $"Hit+10Before-5={hitPlus10BeforeMinus5} " +
                $"Target3Pct1D={target3Pct1DHit} " +
                $"Target5Pct1D={target5Pct1DHit} " +
                $"Target7Pct1D={target7Pct1DHit} " +
                $"Target10Pct1D={target10Pct1DHit} " +
                $"Target15Pct1D={target15Pct1DHit} " +
                $"Errors={errors}");
        }

        private static string BuildOutputCsvFileName(string inputFileName)
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(inputFileName);
            return $"evaluation_{nameWithoutExtension}.csv";
        }
    }
}