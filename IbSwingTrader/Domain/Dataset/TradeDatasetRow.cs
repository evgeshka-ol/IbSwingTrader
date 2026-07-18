namespace IbSwingTrader.Domain.Dataset
{
    public class TradeDatasetRow : TradeRecord
    {
        public bool IsRealTrade { get; set; }
        public int EntryShiftBars { get; set; }
        public decimal? EntryShiftFraction { get; set; }

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

        // Same real Bollinger/MACD/RSI lookback windows the scanner writes to candidates.csv, ending at entry
        public List<decimal> RecentDailyBbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentDailyBbMidBandSeries { get; set; } = [];
        public List<decimal> RecentDailyBbLowerBandSeries { get; set; } = [];
        public List<decimal> RecentDailyRsiSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdLineSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdSignalSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdHistogramSeries { get; set; } = [];

        public List<decimal> RecentWeeklyBbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbMidBandSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbLowerBandSeries { get; set; } = [];
        public List<decimal> RecentWeeklyRsiSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdLineSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdSignalSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdHistogramSeries { get; set; } = [];

        public List<decimal> RecentH4BbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentH4BbMidBandSeries { get; set; } = [];
        public List<decimal> RecentH4BbLowerBandSeries { get; set; } = [];
        public List<decimal> RecentH4RsiSeries { get; set; } = [];
        public List<decimal> RecentH4MacdLineSeries { get; set; } = [];
        public List<decimal> RecentH4MacdSignalSeries { get; set; } = [];
        public List<decimal> RecentH4MacdHistogramSeries { get; set; } = [];
    }
}
