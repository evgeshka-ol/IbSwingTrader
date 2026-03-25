using IbSwingTrader.Models.Settings;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateEvaluationSettingsProvider
    {
        CandidateEvaluationSettings Get();
    }
}