namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateDetails : TickerEntity
    {
        public string CandidateSource { get; set; } = "Primary";

        public List<decimal> RecentDailyMaSeries { get; set; } = [];
        public List<decimal> RecentDailyBbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentDailyBbWidthSeries { get; set; } = [];

        public List<decimal> RecentDailyRsiSeries { get; set; } = [];

        public List<decimal> RecentDailyMacdSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMaSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentWeeklyBbWidthSeries { get; set; } = [];

        public List<decimal> RecentWeeklyRsiSeries { get; set; } = [];

        public List<decimal> RecentWeeklyMacdSeries { get; set; } = [];

        public List<decimal> RecentH4MaSeries { get; set; } = [];
        public List<decimal> RecentH4BbUpperDistanceSeries { get; set; } = [];
        public List<decimal> RecentH4BbWidthSeries { get; set; } = [];

        public List<decimal> RecentH4RsiSeries { get; set; } = [];

        public List<decimal> RecentH4MacdSeries { get; set; } = [];

        public bool IsFromWishlist { get; set; }

        public bool NeedsDeeperEntry { get; set; }

        public bool NeedsMomentumExit { get; set; }

        public required ScanInfo Scan { get; set; }

        public required ScoreInfo Score { get; set; }

        public required MarketContextInfo Context { get; set; }

        public required TradePlanInfo TradePlan { get; set; }

        public CandidateDiagnostics? Diagnostics { get; set; }
    }
}
