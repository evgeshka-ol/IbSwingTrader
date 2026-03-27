
namespace IbSwingTrader.Infrastructure.Settings
{
    public class FeatureCalculationSettingsProvider(IAgentSettingsProvider agentSettingsProvider) : IFeatureCalculationSettingsProvider
    {
        private readonly FeatureCalculationSettings _settings = agentSettingsProvider.Get().FeatureCalculation;

        public FeatureCalculationSettings Get() => _settings;
    }
}
