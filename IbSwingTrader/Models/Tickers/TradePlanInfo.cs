namespace IbSwingTrader.Models.Tickers
{
    public class TradePlanInfo
    {
        public decimal EntryPrice { get; set; }

        public decimal ExitPrice { get; set; }

        public decimal StopLoss { get; set; }

        public decimal ProfitPercent { get; set; }

        public decimal LossPercent { get; set; }
    }
}