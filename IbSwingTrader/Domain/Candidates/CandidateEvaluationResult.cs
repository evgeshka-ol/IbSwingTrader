namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateEvaluationResult
    {
        public required string Ticker { get; set; }
        public DateTime ScanTime { get; set; }
        public DateTime EvaluatedAt { get; set; }

        public required string PresetScanCode { get; set; }
        public string CandidateSource { get; set; } = string.Empty;
        public List<decimal> RecentDailyMaSeries { get; set; } = [];
        public List<decimal> RecentDailyBbMidDistanceSeries { get; set; } = [];
        public List<decimal> RecentDailyBbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentDailyBbWidthSeries { get; set; } = [];
        public List<decimal> RecentDailyBbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentDailyBbMidBandSeries { get; set; } = [];
        public List<decimal> RecentDailyBbLowerBandSeries { get; set; } = [];
        public List<decimal> RecentDailyRsiSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdLineSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdSignalSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdHistogramSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMaSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbMidDistanceSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbWidthSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbMidBandSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbLowerBandSeries { get; set; } = [];
        public List<decimal> RecentWeeklyRsiSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdLineSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdSignalSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdHistogramSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdSeries { get; set; } = [];
        public List<decimal> RecentH4MaSeries { get; set; } = [];
        public List<decimal> RecentH4BbMidDistanceSeries { get; set; } = [];
        public List<decimal> RecentH4BbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentH4BbWidthSeries { get; set; } = [];
        public List<decimal> RecentH4BbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentH4BbMidBandSeries { get; set; } = [];
        public List<decimal> RecentH4BbLowerBandSeries { get; set; } = [];
        public List<decimal> RecentH4RsiSeries { get; set; } = [];
        public List<decimal> RecentH4MacdLineSeries { get; set; } = [];
        public List<decimal> RecentH4MacdSignalSeries { get; set; } = [];
        public List<decimal> RecentH4MacdHistogramSeries { get; set; } = [];
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
        public bool IsFromWishlist { get; set; }
        public int StrategyVersion { get; set; }
        public decimal CandidateScore { get; set; }

        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal ScanPrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal? ScanMovePct { get; set; }
        public decimal? CurrentPct { get; set; }
        public decimal? MaxPct { get; set; }
        public decimal? MinPct { get; set; }
        public decimal? MaxPrice { get; set; }
        public decimal? MinPrice { get; set; }
        public decimal? MaxPctBeforeEntry { get; set; }
        public decimal? MinPctBeforeEntry { get; set; }
        public decimal? MaxPriceBeforeEntry { get; set; }
        public decimal? MinPriceBeforeEntry { get; set; }
        public bool EntryTouched { get; set; }
        public bool ExitTouched { get; set; }
        public bool StopTouched { get; set; }

        public DateTime? MaxTime { get; set; }
        public DateTime? MinTime { get; set; }
        public DateTime? MaxTimeBeforeEntry { get; set; }
        public DateTime? MinTimeBeforeEntry { get; set; }

        public DateTime? EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public DateTime? StopTime { get; set; }

        public bool ExitBeforeStop { get; set; }
        public bool StopBeforeExit { get; set; }

        public decimal MinLowAfterScan { get; set; }
        public decimal EntryDistanceToMinAfterScanPct { get; set; }
        public decimal? EntryUndercutBeforeEntryAbs { get; set; }
        public decimal? EntryUndercutBeforeEntryPct { get; set; }

        public string? Outcome { get; set; }
        public decimal? RealizedPct { get; set; }
        public int? DaysAfterEntry { get; set; }
        public decimal? ExitMissAbs { get; set; }
        public decimal? ExitMissPct { get; set; }
        public bool NearTakeProfitMiss { get; set; }
        public decimal? PostMaxDrawdownPct { get; set; }
        public string? ExtremumOrder { get; set; }
        public int? MinutesFromMinToMax { get; set; }
        public int? MinutesFromEntryToMax { get; set; }
        public int? MinutesFromEntryToMin { get; set; }

        public DateTime? EvaluationStartTime { get; set; }
        public DateTime? EvaluationEndTime { get; set; }
        public bool IsStaleOpen { get; set; }
        public int? OpenAgeDays { get; set; }

        public decimal TakeProfitOverflowPct { get; set; }
        public decimal DaysAfterExitToMaxHigh { get; set; }
    }
}
