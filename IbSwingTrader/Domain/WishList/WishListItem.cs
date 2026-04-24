namespace IbSwingTrader.Domain.WishList
{
    public class WishListItem : TickerEntity
    {
        public required ScanInfo Scan { get; set; }

        public required ScoreInfo Score { get; set; }

        public required MarketContextInfo Context { get; set; }

        public DateTime? FirstSeen { get; set; }

        public DateTime? LastEvaluatedAt { get; set; }

        public DateTime? ExpectedTargetTime { get; set; }

        public int? ExpectedBarsToTarget { get; set; }

        public string? LastStatus { get; set; }

        public string? LastStatusReason { get; set; }

        public DateTime? LastStatusTime { get; set; }
    }
}
