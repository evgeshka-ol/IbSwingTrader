
namespace IbSwingTrader.Abstractions.Logging
{
    public interface ICandidateResultWriter
    {
        Task WriteAsync(string filePath, CandidateSearchResult result);
    }
}
