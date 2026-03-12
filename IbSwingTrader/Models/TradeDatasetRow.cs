namespace IbSwingTrader.Models
{
    public class TradeDatasetRow : TradeRecord
    {
        public bool IsRealTrade { get; set; }

        // --- 4H indicators ---
        public double Pullback5d { get; set; }
        public double Pullback10d { get; set; }
        public double BBPosition { get; set; }
        public double VolumeRatio20 { get; set; }
        public double RSI14 { get; set; }
        public double ATRRatio { get; set; }
        public double MACDHist { get; set; }
        public double TrendPosition { get; set; }

        // --- daily context ---
        public double DailyTrendPosition { get; set; }
        public double DailyPullback10d { get; set; }
        public double DailyRSI14 { get; set; }

        // --- weekly context ---
        public double WeeklyTrendPosition { get; set; }
        public bool WeeklyUptrend { get; set; }

        // --- future movement ---
        public double FutureHigh1d { get; set; }
        public double FutureHigh2d { get; set; }
        public double FutureLow1d { get; set; }
        public double FutureLow2d { get; set; }

        // --- targets ---
        public bool Target10pct1d { get; set; }
        public bool Target10pct2d { get; set; }
    }
}
