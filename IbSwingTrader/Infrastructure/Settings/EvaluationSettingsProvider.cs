using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class EvaluationSettingsProvider : IEvaluationSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider;

        public EvaluationSettingsProvider(IAgentSettingsProvider agentSettingsProvider)
        {
            _agentSettingsProvider = agentSettingsProvider;
        }

        public EvaluationSettings Get()
        {
            return _agentSettingsProvider.Get().Evaluation;
        }
    }
}