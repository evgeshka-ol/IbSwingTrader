namespace IbSwingTrader.Models
{
    public class FeatureSet
    {
        public decimal Pullback10d { get; set; }
        public decimal DistanceTo20dHigh { get; set; }
        public decimal DistanceTo52wHigh { get; set; }
        public decimal VolumeRatio20 { get; set; }
        public decimal ATRRatio { get; set; }
        public decimal TrendPosition { get; set; }
    }
}
