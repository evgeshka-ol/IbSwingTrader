namespace IbSwingTrader.Domain.Settings
{
    public class FeatureCalculationSettings
    {
        public int LookbackCandles { get; set; } = 300;
        public int AvgVolumeWindow { get; set; } = 20;
    }
}
