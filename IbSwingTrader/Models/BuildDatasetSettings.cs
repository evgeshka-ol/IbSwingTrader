namespace IbSwingTrader.Models
{
    public class BuildDatasetSettings
    {
        public int LookbackCandles { get; set; } = 300;
        public int MinimumCandlesRequired { get; set; } = 60;
        public int AvgVolumeWindow { get; set; } = 20;
        public bool IncludeSyntheticTrades { get; set; }
    }
}