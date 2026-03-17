namespace IbSwingTrader.Models
{
    public class FeatureSet
    {
        public decimal Pullback5d { get; set; }
        public decimal Pullback10d { get; set; }
        public decimal DistanceTo20dHigh { get; set; }
        public decimal DistanceTo52wHigh { get; set; }
        public decimal VolumeRatio20 { get; set; }
        public decimal ATRRatio { get; set; }
        public decimal TrendPosition { get; set; }
        public decimal BBPosition { get; set; }
        public decimal BBPositionCentered { get; set; }
        public decimal BBMidSignedDistancePct { get; set; }
        public bool IsBelowBBMid { get; set; }
        public decimal DistanceToBBLowerPct { get; set; }

        public decimal RSI14 { get; set; }

        public decimal MACDHist { get; set; }
        public decimal MACDHistDelta { get; set; }
        public bool MACDHistImproving { get; set; }
    }
}
