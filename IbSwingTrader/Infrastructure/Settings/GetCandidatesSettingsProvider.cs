using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class GetCandidatesSettingsProvider : IGetCandidatesSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider;

        public GetCandidatesSettingsProvider(
            IAgentSettingsProvider agentSettingsProvider)
        {
            _agentSettingsProvider = agentSettingsProvider;
        }

        public GetCandidatesSettings Get()
        {
            return _agentSettingsProvider.Get().GetCandidates;
        }
    }
}