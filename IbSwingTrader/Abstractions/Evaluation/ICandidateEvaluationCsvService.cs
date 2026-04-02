
namespace IbSwingTrader.Abstractions.Evaluation
{
    public interface ICandidateEvaluationCsvService
    {
        Task<List<CandidateEvaluationResult>> ReadAsync(string path);
        Task WriteAsync(string path, List<CandidateEvaluationResult> results);
    }
}
