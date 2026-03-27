namespace IbSwingTrader.Domain.Market
{
    public class FundamentalSnapshot
    {
        public string Ticker { get; set; } = string.Empty;

        public decimal? MarketCap { get; set; }

        public decimal? SharesOutstanding { get; set; }

        public string? Currency { get; set; }

        public string? Sector { get; set; }

        public string? Industry { get; set; }

        public string? Category { get; set; }

        public string RawXml { get; set; } = string.Empty;
    }
}