namespace IbSwingTrader.Infrastructure.Settings
{
    public class ResearchSettingsProvider(
        IAgentSettingsProvider agentSettingsProvider) : IResearchSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider = agentSettingsProvider;

        public ResearchSettings Get() => _agentSettingsProvider.Get().Research;
    }
}
