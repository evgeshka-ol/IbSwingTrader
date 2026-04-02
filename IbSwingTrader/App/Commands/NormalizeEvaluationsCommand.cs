namespace IbSwingTrader.App.Commands
{
    public class NormalizeEvaluationsCommand(
        ICandidateEvaluationCsvService candidateEvaluationCsvService,
        IAgentPathService pathService,
        ITextLogger logger) : ICommand
    {
        private readonly ICandidateEvaluationCsvService _candidateEvaluationCsvService = candidateEvaluationCsvService;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITextLogger _logger = logger;

        public async Task RunAsync()
        {
            var evaluationsPath = _pathService.GetEvaluationsFile();
            var records = await _candidateEvaluationCsvService.ReadAsync(evaluationsPath);

            await _candidateEvaluationCsvService.WriteAsync(evaluationsPath, records);

            _logger.Info($"Evaluations normalized: {evaluationsPath}. Records={records.Count}");
        }
    }
}
