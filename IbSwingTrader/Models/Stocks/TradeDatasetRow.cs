namespace IbSwingTrader.Models.Stocks
{
    public class TradeDatasetRow : TradeRecord
    {
        public bool IsRealTrade { get; set; }
        public int EntryShiftBars { get; set; }

        public bool ExitSameDayOrNextDay => HoldDays <= 1;

        public decimal DistanceTo20dHigh { get; set; }
        public decimal DistanceTo52wHigh { get; set; }

        // Daily series across full hold period
        public List<decimal> DailyMaDistances { get; set; } = [];
        public List<decimal> DailyRsiValues { get; set; } = [];
        public List<decimal> DailyMacdValues { get; set; } = [];

        // Weekly series across full hold period
        public List<decimal> WeeklyMaDistances { get; set; } = [];
        public List<decimal> WeeklyRsiValues { get; set; } = [];
        public List<decimal> WeeklyMacdValues { get; set; } = [];

        // H4 series only for short holds
        public List<decimal>? H4MaDistances { get; set; }
        public List<decimal>? H4RsiValues { get; set; }
        public List<decimal>? H4MacdValues { get; set; }
    }
}