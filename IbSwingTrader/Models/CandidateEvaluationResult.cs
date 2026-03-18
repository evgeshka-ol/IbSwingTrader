using System;
using System.Collections.Generic;
using System.Text;

namespace IbSwingTrader.Models
{
    public class CandidateEvaluationResult
    {
        public required string PresetScanCode { get; set; }
        public string? PresetDescription { get; set; }

        public int TotalCandidates { get; set; }

        public int EntryTouchedCount { get; set; }
        public int WinCount { get; set; }
        public int LossCount { get; set; }
        public int OpenCount { get; set; }
        public int NoEntryCount { get; set; }

        public decimal EntryTouchRatePct { get; set; }
        public decimal WinRatePct { get; set; }
        public decimal LossRatePct { get; set; }

        public decimal AvgRealizedPct { get; set; }
        public decimal AvgMaxMovePct1D { get; set; }
        public decimal AvgMaxMovePct2D { get; set; }
        public decimal AvgMaxMovePct5D { get; set; }

        public decimal AvgMaxDrawdownPct1D { get; set; }
        public decimal AvgMaxDrawdownPct2D { get; set; }
        public decimal AvgMaxDrawdownPct5D { get; set; }

        public decimal Target10Pct1DHitRatePct { get; set; }

        public decimal AvgCandidateScore { get; set; }
    }
}
