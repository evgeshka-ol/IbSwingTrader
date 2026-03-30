namespace IbSwingTrader.Domain.WishList
{
    public class WishListEvaluationRecord
    {
        public required string Ticker { get; set; }

        public DateTime ScanTimeNy { get; set; }

        public DateTime? FirstSeenMarketTime { get; set; }

        public DateTime? PreviousLastEvaluatedMarketTime { get; set; }

        public string? PreviousDecision { get; set; }

        public string? PreviousReason { get; set; }

        public DateTime EvaluatedAtMarketTime { get; set; }

        public string Decision { get; set; } = string.Empty;

        public string? Reason { get; set; }

        public bool RemoveSuggested { get; set; }
    }
}
