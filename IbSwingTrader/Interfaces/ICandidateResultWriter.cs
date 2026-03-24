using IbSwingTrader.Models.Stocks;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateResultWriter
    {
        Task WriteAsync(string filePath, List<CandidateDetails> candidates);
    }
}