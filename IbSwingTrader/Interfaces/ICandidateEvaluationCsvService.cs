using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateEvaluationCsvService
    {
        Task WriteAsync(string path, List<CandidateEvaluationResult> results);
    }
}
