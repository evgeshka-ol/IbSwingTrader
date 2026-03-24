using IbSwingTrader.Models;
using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateEvaluator
    {
        Task<List<CandidateEvaluationResult>> EvaluateAsync(
            List<CandidateDetails> candidates);
    }
}
