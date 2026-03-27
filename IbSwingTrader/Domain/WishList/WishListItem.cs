namespace IbSwingTrader.Domain.WishList
{
    public class WishListItem : TickerEntity
    {
        public required ScanInfo Scan { get; set; }

        public required ScoreInfo Score { get; set; }

        public required MarketContextInfo Context { get; set; }

        public DateTime? FirstSeenMarketTime { get; set; }

        public DateTime? LastEvaluatedMarketTime { get; set; }

        public DateTime? ExpectedTargetMarketTime { get; set; }

        public int? ExpectedBarsToTarget { get; set; }
    }
}