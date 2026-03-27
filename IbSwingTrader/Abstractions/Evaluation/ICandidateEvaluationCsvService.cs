
namespace IbSwingTrader.Abstractions.Evaluation
{
    public interface ICandidateEvaluationCsvService
    {
        Task WriteAsync(string path, List<CandidateEvaluationResult> results);
    }
}
