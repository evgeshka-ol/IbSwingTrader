
namespace IbSwingTrader.Abstractions.Candidates
{
    public interface ICandidateFinder
    {
        Task<CandidateSearchResult> FindAsync();
    }
}