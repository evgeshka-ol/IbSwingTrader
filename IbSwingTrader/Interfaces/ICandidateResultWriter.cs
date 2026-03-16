using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateResultWriter
    {
        Task WriteAsync(List<CandidateDetails> candidates);
    }
}
