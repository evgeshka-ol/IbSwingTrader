namespace IbSwingTrader.Domain.Candidates
{
    public class TradePlanInfo
    {
        public decimal LiveReferencePrice { get; set; }

        public decimal EntryPrice { get; set; }

        public decimal ExitPrice { get; set; }

        public decimal StopLoss { get; set; }

        public decimal StopLimitPrice { get; set; }

        public decimal ProfitPercent { get; set; }

        public decimal LossPercent { get; set; }

        public string? ExitProfile { get; set; }

        public DateTime? ReferencePriceTime { get; set; }
        public DateTime? ReferencePriceBarTime { get; set; }
        public DateTime? ReferencePriceObservedAt { get; set; }
        public string? ReferencePriceSource { get; set; }
        public DateTime? PlanBuiltAt { get; set; }
        public decimal? InitialReferencePrice { get; set; }
        public DateTime? InitialReferencePriceTime { get; set; }
        public DateTime? InitialReferencePriceBarTime { get; set; }
        public DateTime? InitialReferencePriceObservedAt { get; set; }
        public decimal? InitialEntryPrice { get; set; }
        public string? PublicationRefreshStatus { get; set; }
        public DateTime? M5PreviousBarTime { get; set; }
        public decimal? M5PreviousOpen { get; set; }
        public decimal? M5PreviousClose { get; set; }
        public DateTime? M5CurrentBarTime { get; set; }
        public decimal? M5CurrentOpen { get; set; }
        public decimal? M5CurrentClose { get; set; }
        // Shadow forecast only: never used as an execution price before validation.
        public decimal? M5ProjectedEntryPrice { get; set; }
    }
}
