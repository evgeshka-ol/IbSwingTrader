namespace IbSwingTrader.App.Commands
{
    public class NormalizeReportsCommand(
        INormalizeReportsSettingsProvider normalizeReportsSettingsProvider,
        ICandidateFileService candidateFileService,
        NormalizeEvaluationsCommand normalizeEvaluationsCommand,
        IEvaluationDatasetCsvService evaluationDatasetCsvService,
        IAgentPathService pathService,
        ITextLogger logger) : ICommand
    {
        private readonly INormalizeReportsSettingsProvider _normalizeReportsSettingsProvider = normalizeReportsSettingsProvider;
        private readonly ICandidateFileService _candidateFileService = candidateFileService;
        private readonly NormalizeEvaluationsCommand _normalizeEvaluationsCommand = normalizeEvaluationsCommand;
        private readonly IEvaluationDatasetCsvService _evaluationDatasetCsvService = evaluationDatasetCsvService;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITextLogger _logger = logger;

        public async Task RunAsync()
        {
            var settings = _normalizeReportsSettingsProvider.Get();

            if (settings.Candidates)
                await NormalizeCandidatesAsync();

            if (settings.Evaluations)
                await NormalizeEvaluationsAsync();

            if (settings.EvaluationDataset)
                await NormalizeEvaluationDatasetAsync();

            _logger.Info(
                $"Reports normalized. Candidates={settings.Candidates} Evaluations={settings.Evaluations} EvaluationDataset={settings.EvaluationDataset}");
        }

        private async Task NormalizeCandidatesAsync()
        {
            var path = _pathService.GetCandidatesFile();
            var document = await _candidateFileService.ReadAsync(path);
            await _candidateFileService.NormalizeAsync(path);

            _logger.Info(
                $"Candidates report normalized: Reversal={document.Candidates.Count}, TodayResearchLike={document.SameDayCandidates.Count}");
        }

        private async Task NormalizeEvaluationsAsync()
        {
            await _normalizeEvaluationsCommand.RunAsync();
        }

        private async Task NormalizeEvaluationDatasetAsync()
        {
            var path = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), "datasets", "evaluation-dataset.csv"));
            var rows = await _evaluationDatasetCsvService.ReadAsync(path);
            await _evaluationDatasetCsvService.WriteAsync(path, rows);

            _logger.Info($"Evaluation dataset report normalized: Rows={rows.Count}");
        }
    }
}
