namespace IbSwingTrader.Infrastructure.Settings
{
    public class NormalizeReportsSettingsProvider(
        IAgentSettingsProvider agentSettingsProvider) : INormalizeReportsSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider = agentSettingsProvider;

        public NormalizeReportsSettings Get() => _agentSettingsProvider.Get().NormalizeReports;
    }
}
