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
    }
}
