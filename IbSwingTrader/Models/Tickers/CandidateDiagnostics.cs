namespace IbSwingTrader.Models.Tickers
{
    public class CandidateDiagnostics
    {
        public decimal Pullback10d { get; set; }

        public decimal VolumeRatio20 { get; set; }

        public decimal ATRRatio { get; set; }

        public decimal TrendPosition { get; set; }

        public decimal DailyTrendPosition { get; set; }

        public decimal DailyPullback10d { get; set; }

        public decimal BBMidSignedDistancePct { get; set; }

        public decimal? WeeklyMACDHistDelta { get; set; }
    }
}