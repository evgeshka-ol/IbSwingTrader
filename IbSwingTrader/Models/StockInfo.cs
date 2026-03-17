namespace IbSwingTrader.Models
{
    public class StockInfo
    {
        public string Ticker { get; set; } = "";
        public int ConId { get; set; }
        public string Exchange { get; set; } = "";
        public string Currency { get; set; } = "";
        public string TradingClass { get; set; } = "";
        public int Rank { get; set; }

        // Эти поля пока будут заполняться позже, после истории
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal StopLoss { get; set; }
        public string? StockType { get; internal set; }
    }
}
