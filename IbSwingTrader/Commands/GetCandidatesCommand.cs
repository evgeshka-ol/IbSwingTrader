using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Commands
{
    public class GetCandidatesCommand(
        ICandidateFinder finder,
        ICandidateResultWriter writer) : ICommand
    {
        private readonly ICandidateFinder _finder = finder;
        private readonly ICandidateResultWriter _writer = writer;

        public async Task RunAsync(params string[] args)
        {
            var candidates = await _finder.FindAsync();
            await _writer.WriteAsync(candidates);
        }
    }
}
