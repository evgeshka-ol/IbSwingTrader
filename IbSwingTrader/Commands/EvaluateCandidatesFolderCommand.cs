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

        public async Task RunAsync(params string[] args)
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

            var candidateFile = await _jsonFileService.ReadAsync<CandidateFile>(filePath);
            var candidates = candidateFile?.Candidates;

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

        private static string BuildOutputCsvFileName(string inputFileName)
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(inputFileName);
            return $"evaluation_{nameWithoutExtension}.csv";
        }
    }
}