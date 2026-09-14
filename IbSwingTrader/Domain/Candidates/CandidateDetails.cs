namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateDetails : TickerEntity
    {
        public required ScanInfo Scan { get; set; }

        public required TradePlanInfo TradePlan { get; set; }

        public string CandidateSource { get; set; } = "Primary";

        public List<decimal> RecentDailyCloseSeries { get; set; } = [];
        public List<decimal> RecentDailyOpenSeries { get; set; } = [];
        public List<decimal> RecentDailyHighSeries { get; set; } = [];
        public List<decimal> RecentDailyLowSeries { get; set; } = [];
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
        public List<decimal> RecentH4OpenSeries { get; set; } = [];
        public List<decimal> RecentH4HighSeries { get; set; } = [];
        public List<decimal> RecentH4LowSeries { get; set; } = [];
        public List<decimal> RecentH4CloseSeries { get; set; } = [];
        public List<decimal> RecentH4BbUpperBandSeries { get; set; } = [];
        public List<decimal> RecentH4BbMidBandSeries { get; set; } = [];
        public List<decimal> RecentH4BbLowerBandSeries { get; set; } = [];

        public List<decimal> RecentH4RsiSeries { get; set; } = [];

        public List<decimal> RecentH4MacdLineSeries { get; set; } = [];
        public List<decimal> RecentH4MacdSignalSeries { get; set; } = [];
        public List<decimal> RecentH4MacdHistogramSeries { get; set; } = [];
        public string WeeklyBbDirection { get; set; } = string.Empty;
        public string WeeklyBbRegime { get; set; } = string.Empty;

        public string DailyBbDirection { get; set; } = string.Empty;
        public string DailyBbRegime { get; set; } = string.Empty;

        public string H4BbDirection { get; set; } = string.Empty;
        public string H4BbRegime { get; set; } = string.Empty;

        public bool IsFromWishlist { get; set; }

        public bool NeedsDeeperEntry { get; set; }

        public bool NeedsMomentumExit { get; set; }

        public bool IsBellUpPattern { get; set; }

        public string PatternVerdictReason { get; set; } = string.Empty;

        public required ScoreInfo Score { get; set; }

        public required MarketContextInfo Context { get; set; }

        public CandidateDiagnostics? Diagnostics { get; set; }
    }
}
