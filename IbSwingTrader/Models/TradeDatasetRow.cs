namespace IbSwingTrader.Models
{
    public class TradeDatasetRow : TradeRecord
    {
        public bool IsRealTrade { get; set; }
        public int EntryShiftBars { get; set; }

        public bool ExitSameDayOrNextDay => HoldDays <= 1;

        public decimal DistanceTo20dHigh { get; set; }
        public decimal DistanceTo52wHigh { get; set; }

        // Daily MA context
        public decimal DailyMaEntry { get; set; }
        public decimal DailyMaExit { get; set; }
        public decimal DailyMaDelta { get; set; }

        // Weekly MA context
        public decimal? WeeklyMaEntry { get; set; }
        public decimal? WeeklyMaExit { get; set; }
        public decimal? WeeklyMaDelta { get; set; }

        // H4 MA context, only for short holds
        public decimal? H4MaEntry { get; set; }
        public decimal? H4MaExit { get; set; }
        public decimal? H4MaDelta { get; set; }

        // Daily RSI context
        public decimal DailyRsiEntry { get; set; }
        public decimal DailyRsiExit { get; set; }
        public decimal DailyRsiDelta { get; set; }

        // Weekly RSI context
        public decimal? WeeklyRsiEntry { get; set; }
        public decimal? WeeklyRsiExit { get; set; }
        public decimal? WeeklyRsiDelta { get; set; }

        // H4 RSI context, only for short holds
        public decimal? H4RsiEntry { get; set; }
        public decimal? H4RsiExit { get; set; }
        public decimal? H4RsiDelta { get; set; }

        // Daily MACD context
        public decimal DailyMacdEntry { get; set; }
        public decimal DailyMacdExit { get; set; }
        public decimal DailyMacdDelta { get; set; }

        // Weekly MACD context
        public decimal? WeeklyMacdEntry { get; set; }
        public decimal? WeeklyMacdExit { get; set; }
        public decimal? WeeklyMacdDelta { get; set; }

        // H4 MACD context, only for short holds
        public decimal? H4MacdEntry { get; set; }
        public decimal? H4MacdExit { get; set; }
        public decimal? H4MacdDelta { get; set; }
    }
}