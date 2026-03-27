
namespace IbSwingTrader.Infrastructure.Settings
{
    public class TwsSettingsProvider : ITwsSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider;

        public TwsSettingsProvider(IAgentSettingsProvider agentSettingsProvider)
        {
            _agentSettingsProvider = agentSettingsProvider;
        }

        public TwsSettings Get()
        {
            return _agentSettingsProvider.Get().Tws;
        }
    }
}