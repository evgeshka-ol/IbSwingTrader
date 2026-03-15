namespace IbSwingTrader.Models
{
    public class StockInfo
    {
        public required string Ticker { get; set; }

        public decimal Price { get; set; }

        public decimal MarketCap { get; set; }

        public long AvgVolume20 { get; set; }
    }
}
