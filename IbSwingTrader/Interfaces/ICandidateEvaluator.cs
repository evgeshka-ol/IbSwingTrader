using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateEvaluator
    {
        Task<List<CandidateEvaluationResult>> EvaluateAsync(
            List<CandidateDetails> candidates);
    }
}
