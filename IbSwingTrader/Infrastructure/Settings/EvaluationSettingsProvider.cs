using IbSwingTrader.Interfaces;
using IbSwingTrader.Models.Settings;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class CandidateEvaluationSettingsProvider(IAgentSettingsProvider agentSettingsProvider) : ICandidateEvaluationSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider = agentSettingsProvider;

        public CandidateEvaluationSettings Get()
        {
            return _agentSettingsProvider.Get().CandidateEvaluation;
        }
    }
}