namespace IbSwingTrader.Models
{
    public class TradeDatasetRow : TradeRecord
    {
        public bool IsRealTrade { get; set; }

        public int EntryShiftBars { get; set; }

        // --- 4H indicators ---
        public decimal Pullback5d { get; set; }
        public decimal Pullback10d { get; set; }
        public decimal BBPosition { get; set; }
        public decimal VolumeRatio20 { get; set; }
        public decimal RSI14 { get; set; }
        public decimal ATRRatio { get; set; }
        public decimal MACDHist { get; set; }
        public decimal TrendPosition { get; set; }

        // --- daily context ---
        public decimal DailyTrendPosition { get; set; }
        public decimal DailyPullback10d { get; set; }
        public decimal DailyRSI14 { get; set; }

        // --- weekly context ---
        public decimal? WeeklyTrendPosition { get; set; }
        public decimal? WeeklyBBMidSlopePct { get; set; }
        public decimal? WeeklyMACDHistDelta { get; set; }
        public decimal? WeeklyMACDLineMinusSignal { get; set; }

        // --- future movement ---
        public decimal FutureHigh1d { get; set; }
        public decimal FutureHigh2d { get; set; }
        public decimal FutureLow1d { get; set; }
        public decimal FutureLow2d { get; set; }

        public decimal MaxReturn1d { get; set; }
        public decimal MaxReturn2d { get; set; }

        public decimal MaxDrawdown1d { get; set; }
        public decimal MaxDrawdown2d { get; set; }

        public decimal BBPositionCentered { get; set; }
        public decimal BBMidSignedDistancePct { get; set; }
        public bool IsBelowBBMid { get; set; }
        public decimal DistanceToBBLowerPct { get; set; }

        public decimal MACDHistDelta { get; set; }
        public bool MACDHistImproving { get; set; }

        // --- targets ---
        public bool Target10pct1d { get; set; }
        public bool Target10pct2d { get; set; }

        public decimal DistanceTo20dHigh { get; internal set; }
        public decimal DistanceTo52wHigh { get; internal set; }

        // scoring
        public decimal CandidateScore { get; internal set; }
    }
}
