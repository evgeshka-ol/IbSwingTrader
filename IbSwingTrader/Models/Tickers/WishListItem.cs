namespace IbSwingTrader.Models.Tickers
{
    public class WishListItem : TickerEntity
    {
        public required ScanInfo Scan { get; set; }

        public required ScoreInfo Score { get; set; }

        public required MarketContextInfo Context { get; set; }

        public DateTime? FirstSeenNy { get; set; }

        public DateTime? LastEvaluatedNy { get; set; }

        public DateTime? ExpectedTargetTimeNy { get; set; }

        public int? ExpectedBarsToTarget { get; set; }
    }
}