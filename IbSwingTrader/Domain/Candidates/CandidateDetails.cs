namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateDetails : TickerEntity
    {
        public required ScanInfo Scan { get; set; }

        public required TradePlanInfo TradePlan { get; set; }

        public string CandidateSource { get; set; } = "Primary";

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

        public string WeeklyBbDirection { get; set; } = string.Empty;
        public string WeeklyBbRegime { get; set; } = string.Empty;
        public decimal WeeklyBbMidSlope { get; set; }
        public decimal WeeklyBbWidthSlope { get; set; }
        public decimal WeeklyBbUpperDistanceSlope { get; set; }

        public string DailyBbDirection { get; set; } = string.Empty;
        public string DailyBbRegime { get; set; } = string.Empty;
        public decimal DailyBbMidSlope { get; set; }
        public decimal DailyBbWidthSlope { get; set; }
        public decimal DailyBbUpperDistanceSlope { get; set; }

        public string H4BbDirection { get; set; } = string.Empty;
        public string H4BbRegime { get; set; } = string.Empty;
        public decimal H4BbMidSlope { get; set; }
        public decimal H4BbWidthSlope { get; set; }
        public decimal H4BbUpperDistanceSlope { get; set; }

        public bool IsFromWishlist { get; set; }

        public bool NeedsDeeperEntry { get; set; }

        public bool NeedsMomentumExit { get; set; }

        public required ScoreInfo Score { get; set; }

        public required MarketContextInfo Context { get; set; }

        public CandidateDiagnostics? Diagnostics { get; set; }
    }
}
