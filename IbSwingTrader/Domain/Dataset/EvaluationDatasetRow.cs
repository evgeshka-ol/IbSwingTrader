namespace IbSwingTrader.Domain.Dataset
{
    public class EvaluationDatasetRow : TickerEntity
    {
        public DateTime ScanTime { get; set; }

        public DateTime? EntryTime { get; set; }

        public DateTime? ExitTime { get; set; }

        public string Outcome { get; set; } = string.Empty;

        public decimal AmplitudePct { get; set; }

        public decimal ScanPrice { get; set; }

        public decimal EntryPrice { get; set; }

        public decimal ExitPrice { get; set; }

        public decimal StopLoss { get; set; }

        public decimal PlannedProfitPct { get; set; }

        public decimal PlannedLossPct { get; set; }

        public decimal? MaxPct { get; set; }

        public decimal? MaxPrice { get; set; }

        public DateTime? MaxTime { get; set; }

        public decimal? MinPct { get; set; }

        public decimal? MinPrice { get; set; }

        public DateTime? MinTime { get; set; }

        public decimal? ScanMovePct { get; set; }

        public decimal? CurrentPct { get; set; }

        public required string PresetScanCode { get; set; }
        public string ExtremumOrder { get; set; } = string.Empty;

        public string MinDepthGroup { get; set; } = string.Empty;

        public string MaxStrengthGroup { get; set; } = string.Empty;

        public string ExtremumSubgroup { get; set; } = string.Empty;

        public required string GroupLabel { get; set; }

        public string CandidateSource { get; set; } = string.Empty;

        public string CandidateGroup { get; set; } = string.Empty;

        public int? CandidateDisplayRank { get; set; }

        public int? DaysAfterEntry { get; set; }


        public bool IsFromWishlist { get; set; }

        public int StrategyVersion { get; set; }

        public DateTime EvaluatedAt { get; set; }
        public bool IsStaleOpen { get; set; }
        public int? OpenAgeDays { get; set; }

        public decimal EntryDistanceToMinAfterScanPct { get; set; }

        public decimal? MaxPctBeforeEntry { get; set; }

        public decimal? MaxPriceBeforeEntry { get; set; }

        public DateTime? MaxTimeBeforeEntry { get; set; }

        public decimal? MinPctBeforeEntry { get; set; }

        public decimal? MinPriceBeforeEntry { get; set; }

        public DateTime? MinTimeBeforeEntry { get; set; }

        public decimal? EntryUndercutBeforeEntryAbs { get; set; }

        public decimal? EntryUndercutBeforeEntryPct { get; set; }

        public decimal PositivePotentialPct { get; set; }

        public decimal NegativePotentialPct { get; set; }

        public int? DaysToMaxUpFromScan { get; set; }

        public int? DaysToMaxUpFromEntry { get; set; }

        public int? DaysToMaxDownFromScan { get; set; }

        public int? DaysToMaxDownFromEntry { get; set; }

        public decimal? ExitMissAbs { get; set; }

        public decimal? ExitMissPct { get; set; }

        public bool NearTakeProfitMiss { get; set; }

        public decimal? PostMaxDrawdownPct { get; set; }

        public int? MinutesFromMinToMax { get; set; }

        public int? MinutesFromEntryToMax { get; set; }

        public int? MinutesFromEntryToMin { get; set; }

        public bool? MaxDownBeforeMaxUp { get; set; }

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

        public List<decimal> RecentDailyMaSeries { get; set; } = [];
        public List<decimal> RecentDailyBbMidDistanceSeries { get; set; } = [];
        public List<decimal> RecentDailyBbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentDailyBbWidthSeries { get; set; } = [];
        public List<decimal> RecentDailyBbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentDailyBbMidBandSeries { get; set; } = [];
        public List<decimal> RecentDailyBbLowerBandSeries { get; set; } = [];

        public List<decimal> RecentDailyRsiSeries { get; set; } = [];

        public List<decimal> RecentDailyMacdSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMaSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbMidDistanceSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbWidthSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbMidBandSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbLowerBandSeries { get; set; } = [];

        public List<decimal> RecentWeeklyRsiSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMacdSeries { get; set; } = [];

        public List<decimal> RecentH4MaSeries { get; set; } = [];
        public List<decimal> RecentH4BbMidDistanceSeries { get; set; } = [];
        public List<decimal> RecentH4BbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentH4BbWidthSeries { get; set; } = [];
        public List<decimal> RecentH4BbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentH4BbMidBandSeries { get; set; } = [];
        public List<decimal> RecentH4BbLowerBandSeries { get; set; } = [];

        public List<decimal> RecentH4RsiSeries { get; set; } = [];

        public List<decimal> RecentH4MacdSeries { get; set; } = [];

        public string WeeklyBbDirection { get; set; } = string.Empty;
        public string WeeklyBbRegime { get; set; } = string.Empty;
        public decimal WeeklyBbMidSlope { get; set; }
        public decimal WeeklyBbWidthSlope { get; set; }
        public decimal WeeklyBbUpperDistanceSlope { get; set; }

        public string DailyBbDirection { get; set; } = string.Empty;
        public string DailyBbRegime { get; set; } = string.Empty;
        public decimal DailyBbMidSlope { get; set; }
        public decimal DailyBbWidthSlope { get; set; }
        public decimal DailyBbUpperDistanceSlope { get; set; }

        public string H4BbDirection { get; set; } = string.Empty;
        public string H4BbRegime { get; set; } = string.Empty;
        public decimal H4BbMidSlope { get; set; }
        public decimal H4BbWidthSlope { get; set; }
        public decimal H4BbUpperDistanceSlope { get; set; }
    }
}
