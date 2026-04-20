namespace IbSwingTrader.Infrastructure.Settings
{
    public class BuildEvaluationDatasetSettingsProvider(
        IAgentSettingsProvider agentSettingsProvider) : IBuildEvaluationDatasetSettingsProvider
    {
        private readonly IAgentSettingsProvider _agentSettingsProvider = agentSettingsProvider;

        public BuildEvaluationDatasetSettings Get() => _agentSettingsProvider.Get().BuildEvaluationDataset;
    }
}
