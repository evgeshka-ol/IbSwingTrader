using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateEvaluationSettingsProvider
    {
        CandidateEvaluationSettings Get();
    }
}