using System;
using System.Collections.Generic;
using System.Text;

namespace IbSwingTrader.Models
{
    public class CandidateEvaluationResult
    {
        public string Ticker { get; set; }
        public DateTime ScanTime { get; set; }

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

        public bool Target10Pct1DHit { get; set; }
    }
}
