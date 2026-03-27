
namespace IbSwingTrader.Infrastructure.Settings
{
    public class BuildDatasetSettingsProvider(IAgentSettingsProvider agentSettingsProvider) : IBuildDatasetSettingsProvider
    {
        private readonly BuildDatasetSettings _settings = agentSettingsProvider.Get().BuildDataset;

        public BuildDatasetSettings Get() => _settings;
    }
}
