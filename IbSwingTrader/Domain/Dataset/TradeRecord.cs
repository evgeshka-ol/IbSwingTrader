namespace IbSwingTrader.Domain.Dataset
{
    public class TradeRecord : TickerEntity
    {
        public bool IsShort { get; set; }

        public DateTime EntryDate => EntryTimeMarket.Date;

        public DateTime EntryTimeMarket { get; set; }
        public decimal EntryPrice { get; set; }

        public DateTime ExitTimeMarket { get; set; }
        public decimal ExitPrice { get; set; }

        public decimal ProfitPercent { get; set; }

        public int HoldDays { get; set; }
    }
}
