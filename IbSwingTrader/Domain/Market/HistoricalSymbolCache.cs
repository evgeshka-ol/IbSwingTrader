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

        public List<decimal> RecentDailyRsiSeries { get; set; } = [];

        public List<decimal> RecentDailyMacdSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMaSeries { get; set; } = [];

        public List<decimal> RecentWeeklyRsiSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMacdSeries { get; set; } = [];

        public List<decimal> RecentH4MaSeries { get; set; } = [];

        public List<decimal> RecentH4RsiSeries { get; set; } = [];

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
