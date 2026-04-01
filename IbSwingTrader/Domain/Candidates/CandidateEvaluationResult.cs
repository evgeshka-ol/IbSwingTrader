namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateEvaluationResult
    {
        public required string Ticker { get; set; }
        public DateTime ScanTimeNy { get; set; }

        public required string PresetScanCode { get; set; }
        public int StrategyVersion { get; set; }
        public decimal CandidateScore { get; set; }

        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal StopLoss { get; set; }

        public bool EntryTouched { get; set; }
        public bool ExitTouched { get; set; }
        public bool StopTouched { get; set; }

        public DateTime? EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }
        public DateTime? StopTime { get; set; }

        public bool ExitBeforeStop { get; set; }
        public bool StopBeforeExit { get; set; }

        public decimal MaxHighAfterScan1D { get; set; }
        public decimal MaxHighAfterScan2D { get; set; }
        public decimal MaxHighAfterScan5D { get; set; }

        public decimal MaxMovePct1D { get; set; }
        public decimal MaxMovePct2D { get; set; }
        public decimal MaxMovePct5D { get; set; }

        public decimal MaxDrawdownPct1D { get; set; }
        public decimal MaxDrawdownPct2D { get; set; }
        public decimal MaxDrawdownPct5D { get; set; }

        public decimal MinLowAfterScan { get; set; }
        public decimal EntryDistanceToMinAfterScanPct { get; set; }

        public string? Outcome { get; set; }
        public decimal? RealizedPct { get; set; }

        public DateTime? EvaluationStartTime { get; set; }
        public DateTime? EvaluationEndTime { get; set; }

        public decimal MaxHighAfterEntry { get; set; }
        public decimal ExitDistanceToMaxAfterEntryPct { get; set; }

        public decimal MaxHighAfterExit { get; set; }
        public decimal TakeProfitOverflowPct { get; set; }
        public decimal DaysAfterExitToMaxHigh { get; set; }

        public decimal MaxHighAfterEntry1D { get; set; }
        public decimal MinLowAfterEntry1D { get; set; }

        public decimal MaxHighAfterEntry2D { get; set; }
        public decimal MinLowAfterEntry2D { get; set; }

        public decimal MaxHighAfterEntry5D { get; set; }
        public decimal MinLowAfterEntry5D { get; set; }

        public bool Target3Pct1DHit { get; set; }
        public bool Target5Pct1DHit { get; set; }
        public bool Target7Pct1DHit { get; set; }
        public bool Target10Pct1DHit { get; set; }
        public bool Target15Pct1DHit { get; set; }

        public bool Target3Pct2DHit { get; set; }
        public bool Target5Pct2DHit { get; set; }
        public bool Target7Pct2DHit { get; set; }
        public bool Target10Pct2DHit { get; set; }
        public bool Target15Pct2DHit { get; set; }

        public bool Target3Pct5DHit { get; set; }
        public bool Target5Pct5DHit { get; set; }
        public bool Target7Pct5DHit { get; set; }
        public bool Target10Pct5DHit { get; set; }
        public bool Target15Pct5DHit { get; set; }

        public bool? HitPlus5BeforeMinus5 { get; set; }
        public bool? HitPlus7BeforeMinus5 { get; set; }
        public bool? HitPlus10BeforeMinus5 { get; set; }
    }
}
