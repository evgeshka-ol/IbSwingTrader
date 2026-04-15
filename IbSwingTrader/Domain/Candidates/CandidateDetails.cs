namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateDetails : TickerEntity
    {
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
