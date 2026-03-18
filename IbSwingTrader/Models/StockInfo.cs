namespace IbSwingTrader.Models
{
    public class StockInfo
    {
        public string PresetScanCode { get; set; }
        public string PresetScanCode { get; set; }

        public string Ticker { get; set; } = "";
        public int ConId { get; set; }
        public string Exchange { get; set; } = "";
        public string Currency { get; set; } = "";
        public string TradingClass { get; set; } = "";
        public int Rank { get; set; }
        public string? StockType { get; internal set; }
    }
}
