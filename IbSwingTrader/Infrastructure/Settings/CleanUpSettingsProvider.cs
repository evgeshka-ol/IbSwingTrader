namespace IbSwingTrader.Infrastructure.Settings
{
    public class CleanUpSettingsProvider(
        IAgentSettingsProvider agentSettingsProvider) : ICleanUpSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider = agentSettingsProvider;

        public CleanUpSettings Get() => _agentSettingsProvider.Get().CleanUp;
    }
}
