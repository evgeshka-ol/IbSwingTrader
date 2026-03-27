
namespace IbSwingTrader.Abstractions.Evaluation
{
    public interface ICandidateEvaluator
    {
        Task<List<CandidateEvaluationResult>> EvaluateAsync(
            List<CandidateDetails> candidates);
    }
}
