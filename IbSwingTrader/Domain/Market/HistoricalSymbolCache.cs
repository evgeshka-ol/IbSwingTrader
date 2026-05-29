namespace IbSwingTrader.Domain.Market
{
    public class HistoricalSymbolCache
    {
        public string Symbol { get; set; } = string.Empty;

        public Dictionary<string, List<Candle>> Timeframes { get; set; } = new();

        public Dictionary<string, HistoricalTimeframeCoverage> Coverage { get; set; } = new();

        public CachedPatternSnapshot? PatternSnapshot { get; set; }
    }

    public class HistoricalTimeframeCoverage
    {
        public int Count { get; set; }

        public DateTime? FirstTime { get; set; }

        public DateTime? LastTime { get; set; }

        public int SpanDays { get; set; }
    }

    public class CachedPatternSnapshot
    {
        public List<decimal> RecentDailyMaSeries { get; set; } = [];
        public List<decimal> RecentDailyBbMidDistanceSeries { get; set; } = [];
        public List<decimal> RecentDailyBbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentDailyBbWidthSeries { get; set; } = [];
        public List<decimal> RecentDailyBbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentDailyBbMidBandSeries { get; set; } = [];
        public List<decimal> RecentDailyBbLowerBandSeries { get; set; } = [];

        public List<decimal> RecentDailyRsiSeries { get; set; } = [];

        public List<decimal> RecentDailyMacdLineSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdSignalSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdHistogramSeries { get; set; } = [];
        public List<decimal> RecentDailyMacdSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMaSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbMidDistanceSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbWidthSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbMidBandSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbLowerBandSeries { get; set; } = [];

        public List<decimal> RecentWeeklyRsiSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMacdLineSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdSignalSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdHistogramSeries { get; set; } = [];
        public List<decimal> RecentWeeklyMacdSeries { get; set; } = [];

        public List<decimal> RecentH4MaSeries { get; set; } = [];
        public List<decimal> RecentH4BbMidDistanceSeries { get; set; } = [];
        public List<decimal> RecentH4BbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentH4BbWidthSeries { get; set; } = [];
        public List<decimal> RecentH4BbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentH4BbMidBandSeries { get; set; } = [];
        public List<decimal> RecentH4BbLowerBandSeries { get; set; } = [];

        public List<decimal> RecentH4RsiSeries { get; set; } = [];

        public List<decimal> RecentH4MacdLineSeries { get; set; } = [];
        public List<decimal> RecentH4MacdSignalSeries { get; set; } = [];
        public List<decimal> RecentH4MacdHistogramSeries { get; set; } = [];
        public List<decimal> RecentH4MacdSeries { get; set; } = [];

        public decimal DailyMaSlope { get; set; }

        public decimal DailyRsiSlope { get; set; }

        public decimal H4MaSlope { get; set; }

        public decimal H4RsiSlope { get; set; }

        public int DailyRsiUpMoves { get; set; }

        public int H4RsiUpMoves { get; set; }

        public bool H4MaRollingOver { get; set; }

        public bool H4RsiExhausted { get; set; }
    }
}
