
namespace IbSwingTrader.Infrastructure.Settings
{
    public class MarketSessionSettingsProvider(
        IAgentSettingsProvider agentSettingsProvider) : IMarketSessionSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider = agentSettingsProvider;

        public MarketSessionsSettings Get()
        {
            return _agentSettingsProvider.Get().MarketSessions ?? new MarketSessionsSettings();
        }
    }
}