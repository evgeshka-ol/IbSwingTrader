namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateEvaluationResult
    {
        public required string Ticker { get; set; }
        public DateTime ScanTimeMarket { get; set; }
        public DateTime EvaluatedAtMarketTime { get; set; }

        public required string PresetScanCode { get; set; }
        public int StrategyVersion { get; set; }
        public decimal CandidateScore { get; set; }

        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal ScanPrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal? ScanMovePct { get; set; }
        public decimal? CurrentPct { get; set; }
        public decimal? MaxUpPct { get; set; }
        public DateTime? MaxUpTime { get; set; }
        public decimal? MaxDownPct { get; set; }
        public DateTime? MaxDownTime { get; set; }

        public bool EntryTouched { get; set; }
        public bool ExitTouched { get; set; }
        public bool StopTouched { get; set; }

        public DateTime? EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public DateTime? StopTime { get; set; }

        public bool ExitBeforeStop { get; set; }
        public bool StopBeforeExit { get; set; }

        public decimal MinLowAfterScan { get; set; }
        public decimal EntryDistanceToMinAfterScanPct { get; set; }

        public string? Outcome { get; set; }
        public decimal? RealizedPct { get; set; }
        public int? DaysAfterEntry { get; set; }

        public DateTime? EvaluationStartTime { get; set; }
        public DateTime? EvaluationEndTime { get; set; }

        public decimal TakeProfitOverflowPct { get; set; }
        public decimal DaysAfterExitToMaxHigh { get; set; }
    }
}
