using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateFinder
    {
        Task<CandidateSearchResult> FindAsync();
    }
}