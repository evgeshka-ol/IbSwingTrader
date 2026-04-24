namespace IbSwingTrader.Domain.WishList
{
    public class WishListEvaluationResult
    {
        public required string Ticker { get; set; }

        public DateTime ScanTime { get; set; }

        public bool RemoveFromWishList { get; set; }

        public string Decision { get; set; } = string.Empty;

        public string? Reason { get; set; }
    }
}
