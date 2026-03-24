namespace IbSwingTrader.Models.Tickers
{
    public class MarketContextInfo
    {
        public decimal DistanceTo20dHigh { get; set; }

        public decimal DistanceTo52wHigh { get; set; }

        public decimal DailyRSI14 { get; set; }

        public string? Notes { get; set; }
    }
}