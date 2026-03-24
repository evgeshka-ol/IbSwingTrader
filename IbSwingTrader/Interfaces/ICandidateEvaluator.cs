using IbSwingTrader.Models;
using IbSwingTrader.Models.Stocks;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateEvaluator
    {
        Task<List<CandidateEvaluationResult>> EvaluateAsync(
            List<CandidateDetails> candidates);
    }
}
