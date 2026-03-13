namespace IbSwingTrader.Models
{
    public class TradeRecord
    {
        public string Ticker { get; set; } = "";
        public bool IsShort { get; set; }

        public DateTime EntryDate => EntryTimeUtc.Date;

        public DateTime EntryTimeUtc { get; set; }
        public decimal EntryPrice { get; set; }

        public DateTime ExitTimeUtc { get; set; }
        public decimal ExitPrice { get; set; }

        public decimal ProfitPercent { get; set; }

        public int HoldDays { get; set; }
    }
}
