namespace IbSwingTrader.Models.Tickers
{
    public class WishListItem : ScannedTickerBase
    {
        public DateTime? FirstSeenNy { get; set; }

        public DateTime? LastEvaluatedNy { get; set; }

        public DateTime? ExpectedTargetTimeNy { get; set; }

        public int? ExpectedBarsToTarget { get; set; }
    }
}
