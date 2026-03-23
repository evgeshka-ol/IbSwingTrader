using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class GetCandidatesSettingsProvider(
        IAgentSettingsProvider agentSettingsProvider) : IGetCandidatesSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider = agentSettingsProvider;

        public GetCandidatesSettings Get()
        {
            return _agentSettingsProvider.Get().GetCandidates;
        }
    }
}