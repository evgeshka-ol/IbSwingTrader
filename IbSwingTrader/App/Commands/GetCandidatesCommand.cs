namespace IbSwingTrader.App.Commands
{
    public class GetCandidatesCommand(
        ICandidateFinder finder,
        ICandidateResultWriter candidateWriter,
        IAgentPathService pathService,
        ITextLogger logger) : ICommand
    {
        private readonly ICandidateFinder _finder = finder;
        private readonly ICandidateResultWriter _candidateWriter = candidateWriter;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITextLogger _logger = logger;

        public async Task RunAsync()
        {
            var result = await _finder.FindAsync();
            var candidatesPath = _pathService.GetCandidatesFile();

            var candidatesFolder = Path.GetDirectoryName(candidatesPath);
            if (!string.IsNullOrWhiteSpace(candidatesFolder))
                Directory.CreateDirectory(candidatesFolder);

            await _candidateWriter.WriteAsync(candidatesPath, result);

            _logger.Info($"Candidates saved: {candidatesPath}");
            _logger.Info(
                $"GetCandidates completed. " +
                $"Candidates count: {result.Candidates.Count}, " +
                $"Same-day count: {result.SameDayCandidates.Count}");
        }
    }
}
