namespace IbSwingTrader.Models.Tickers
{
    public class CandidateDetails : Candidate
    {
        public required string PresetScanCode { get; set; }
        public required string PresetDescription { get; set; }

        public decimal Pullback10d { get; set; }

        public decimal DistanceTo20dHigh { get; set; }

        public decimal DistanceTo52wHigh { get; set; }

        public decimal VolumeRatio20 { get; set; }

        public decimal ATRRatio { get; set; }

        public decimal TrendPosition { get; set; }

        public decimal DailyTrendPosition { get; internal set; }
        public decimal DailyPullback10d { get; internal set; }
        public decimal DailyRSI14 { get; internal set; }
        public decimal BBMidSignedDistancePct { get; internal set; }
        public decimal? WeeklyMACDHistDelta { get; internal set; }

        // New York time only
        public DateTime ScanTimeMarket { get; set; }

        public string ScanTimeZone { get; set; } = string.Empty;

        public bool IsWishList { get; set; }

        public DateTime? FirstSeenNy { get; set; }

        public DateTime? LastEvaluatedNy { get; set; }

        public DateTime? ExpectedTargetTimeNy { get; set; }

        public int? ExpectedBarsToTarget { get; set; }

        public decimal? WeeklyScore { get; set; }

        public decimal? DailyScore { get; set; }

        public decimal? EntryScore { get; set; }

        public string? Notes { get; set; }
    }
}
