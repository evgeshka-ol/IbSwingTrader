using IbSwingTrader.Models.Settings;

namespace IbSwingTrader.Interfaces
{
    public interface IFeatureCalculationSettingsProvider
    {
        FeatureCalculationSettings Get();
    }
}
