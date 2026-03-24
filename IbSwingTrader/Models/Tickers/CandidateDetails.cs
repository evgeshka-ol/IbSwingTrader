namespace IbSwingTrader.Models.Tickers
{
    public class CandidateDetails : TickerEntity
    {
        public required ScanInfo Scan { get; set; }

        public required ScoreInfo Score { get; set; }

        public required MarketContextInfo Context { get; set; }

        public required TradePlanInfo TradePlan { get; set; }

        public CandidateDiagnostics? Diagnostics { get; set; }
    }
}