namespace IbSwingTrader.Models
{
    public class TradeRecord
    {
        public string Ticker { get; set; } = "";

        public DateTime EntryTimeUtc { get; set; }
        public decimal EntryPrice { get; set; }

        public DateTime ExitTimeUtc { get; set; }
        public decimal ExitPrice { get; set; }

        public decimal ProfitPercent { get; set; }

        public int HoldDays { get; set; }
    }
}
