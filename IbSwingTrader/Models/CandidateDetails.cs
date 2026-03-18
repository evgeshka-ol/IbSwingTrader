namespace IbSwingTrader.Models
{
    public class CandidateDetails : Candidate
    {
        public decimal Pullback10d { get; set; }

        public decimal DistanceTo20dHigh { get; set; }

        public decimal DistanceTo52wHigh { get; set; }

        public decimal VolumeRatio20 { get; set; }

        public decimal ATRRatio { get; set; }

        public decimal TrendPosition { get; set; }

        public DateTime ScanTime { get; set; }
        public decimal DailyTrendPosition { get; internal set; }
        public decimal DailyPullback10d { get; internal set; }
        public decimal DailyRSI14 { get; internal set; }
        public decimal BBMidSignedDistancePct { get; internal set; }
        public decimal? WeeklyMACDHistDelta { get; internal set; }
    }
}
