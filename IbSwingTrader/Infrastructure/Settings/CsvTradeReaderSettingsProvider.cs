using IbSwingTrader.Interfaces;
using IbSwingTrader.Models.Settings;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class CsvTradeReaderSettingsProvider : ICsvTradeReaderSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider;

        public CsvTradeReaderSettingsProvider(
            IAgentSettingsProvider agentSettingsProvider)
        {
            _agentSettingsProvider = agentSettingsProvider;
        }

        public CsvTradeReaderSettings Get()
        {
            return _agentSettingsProvider.Get().CsvTradeReader;
        }
    }
}