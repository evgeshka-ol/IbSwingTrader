namespace IbSwingTrader.Domain.Dataset
{
    public class EvaluationDatasetRow : TickerEntity
    {
        public DateTime ScanTimeMarket { get; set; }

        public required string PresetScanCode { get; set; }

        public bool IsFromWishlist { get; set; }

        public string Outcome { get; set; } = string.Empty;

        public int StrategyVersion { get; set; }

        public DateTime EvaluatedAtMarketTime { get; set; }

        public DateTime? EntryTime { get; set; }

        public int? DaysAfterEntry { get; set; }

        public decimal EntryPrice { get; set; }

        public decimal ExitPrice { get; set; }

        public decimal StopLoss { get; set; }

        public decimal PlannedProfitPct { get; set; }

        public decimal PlannedLossPct { get; set; }

        public decimal? ScanMovePct { get; set; }

        public decimal? CurrentPct { get; set; }

        public decimal EntryDistanceToMinAfterScanPct { get; set; }

        public decimal? MaxUpPct { get; set; }

        public DateTime? MaxUpTime { get; set; }

        public decimal? MaxDownPct { get; set; }

        public DateTime? MaxDownTime { get; set; }

        public decimal PositivePotentialPct { get; set; }

        public decimal NegativePotentialPct { get; set; }

        public decimal AmplitudePct { get; set; }

        public int? DaysToMaxUpFromScan { get; set; }

        public int? DaysToMaxUpFromEntry { get; set; }

        public int? DaysToMaxDownFromScan { get; set; }

        public int? DaysToMaxDownFromEntry { get; set; }

        public bool? MaxDownBeforeMaxUp { get; set; }

        public required string GroupLabel { get; set; }

        public bool HasActiveCandidateSnapshot { get; set; }

        public decimal? CandidateScore { get; set; }

        public decimal? WeeklyScore { get; set; }

        public decimal? DailyScore { get; set; }

        public decimal? EntryScore { get; set; }

        public decimal? DistanceTo20dHigh { get; set; }

        public decimal? DistanceTo52wHigh { get; set; }

        public decimal? DailyRsi14 { get; set; }

        public decimal? Pullback10d { get; set; }

        public decimal? DailyPullback10d { get; set; }

        public decimal? VolumeRatio20 { get; set; }

        public decimal? AtrRatio { get; set; }

        public decimal? TrendPosition { get; set; }

        public decimal? DailyTrendPosition { get; set; }

        public decimal? BbMidSignedDistancePct { get; set; }

        public decimal? WeeklyMacdHistDelta { get; set; }
    }
}
