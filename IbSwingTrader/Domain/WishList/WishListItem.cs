namespace IbSwingTrader.Domain.WishList
{
    public class WishListItem : TickerEntity
    {
        public required ScanInfo Scan { get; set; }

        public required ScoreInfo Score { get; set; }

        public required MarketContextInfo Context { get; set; }

        public List<decimal> RecentDailyBbUpperBandSeries { get; set; } = [];

        public List<decimal> RecentDailyBbMidBandSeries { get; set; } = [];

        public List<decimal> RecentDailyBbLowerBandSeries { get; set; } = [];

        public List<decimal> RecentDailyMacdLineSeries { get; set; } = [];

        public List<decimal> RecentDailyMacdSignalSeries { get; set; } = [];

        public List<decimal> RecentDailyMacdHistogramSeries { get; set; } = [];

        public List<decimal> RecentDailyRsiSeries { get; set; } = [];

        public List<decimal> RecentWeeklyBbUpperBandSeries { get; set; } = [];

        public List<decimal> RecentWeeklyBbMidBandSeries { get; set; } = [];

        public List<decimal> RecentWeeklyBbLowerBandSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMacdLineSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMacdSignalSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMacdHistogramSeries { get; set; } = [];

        public List<decimal> RecentWeeklyRsiSeries { get; set; } = [];

        public List<decimal> RecentH4BbUpperBandSeries { get; set; } = [];

        public List<decimal> RecentH4BbMidBandSeries { get; set; } = [];

        public List<decimal> RecentH4BbLowerBandSeries { get; set; } = [];

        public List<decimal> RecentH4MacdLineSeries { get; set; } = [];

        public List<decimal> RecentH4MacdSignalSeries { get; set; } = [];

        public List<decimal> RecentH4MacdHistogramSeries { get; set; } = [];

        public List<decimal> RecentH4RsiSeries { get; set; } = [];

        public DateTime? FirstSeen { get; set; }

        public DateTime? LastEvaluatedAt { get; set; }

        public DateTime? ExpectedTargetTime { get; set; }

        public int? ExpectedBarsToTarget { get; set; }

        public string? LastStatus { get; set; }

        public string? LastStatusReason { get; set; }

        public DateTime? LastStatusTime { get; set; }
    }
}
