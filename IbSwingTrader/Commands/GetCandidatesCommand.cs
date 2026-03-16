using IbSwingTrader.Interfaces;
using IbSwingTrader.Services;

namespace IbSwingTrader.Commands
{
    public class GetCandidatesCommand(
        CandidateFinder finder,
        ICandidateResultWriter writer)
    {
        private readonly CandidateFinder _finder = finder;
        private readonly ICandidateResultWriter _writer = writer;

        public async Task RunAsync()
        {
            var candidates = await _finder.FindAsync();
            await _writer.WriteAsync(candidates);
        }
    }
}
