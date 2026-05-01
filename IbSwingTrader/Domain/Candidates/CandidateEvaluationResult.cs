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
        public List<decimal> RecentDailyRsiSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMaSeries { get; set; } = [];
        public List<decimal> RecentWeeklyRsiSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdSeries { get; set; } = [];
        public List<decimal> RecentH4MaSeries { get; set; } = [];
        public List<decimal> RecentH4RsiSeries { get; set; } = [];
        public List<decimal> RecentH4MacdSeries { get; set; } = [];
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
