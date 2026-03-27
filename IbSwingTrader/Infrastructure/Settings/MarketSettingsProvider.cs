
namespace IbSwingTrader.Infrastructure.Settings
{
    public class MarketSettingsProvider : IMarketSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider;

        public MarketSettingsProvider(
            IAgentSettingsProvider agentSettingsProvider)
        {
            _agentSettingsProvider = agentSettingsProvider;
        }

        public MarketSettings Get()
        {
            return _agentSettingsProvider.Get().Market;
        }
    }
}