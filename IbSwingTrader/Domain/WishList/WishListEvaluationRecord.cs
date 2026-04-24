namespace IbSwingTrader.Domain.WishList
{
    public class WishListEvaluationRecord
    {
        public required string Ticker { get; set; }

        public DateTime ScanTime { get; set; }

        public DateTime? FirstSeen { get; set; }

        public DateTime? PreviousLastEvaluatedAt { get; set; }

        public string? PreviousDecision { get; set; }

        public string? PreviousReason { get; set; }

        public DateTime EvaluatedAt { get; set; }

        public string Decision { get; set; } = string.Empty;

        public string? Reason { get; set; }

        public bool RemoveSuggested { get; set; }
    }
}
