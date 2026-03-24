namespace IbSwingTrader.Models.Stocks
{
    public class TradeRecord : TickerEntity
    {
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
