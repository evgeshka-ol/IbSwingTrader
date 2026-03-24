namespace IbSwingTrader.Models.Tickers
{
    public class Candidate
    {
        public required string Ticker { get; set; }

        public decimal EntryPrice { get; set; }

        public decimal ExitPrice { get; set; }

        public decimal StopLoss { get; set; }

        public decimal ProfitPercent { get; set; }

        public decimal LossPercent { get; set; }

        public decimal Score { get; set; }
    }
}
