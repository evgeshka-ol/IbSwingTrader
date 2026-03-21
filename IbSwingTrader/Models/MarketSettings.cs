namespace IbSwingTrader.Models
{
    public class MarketSettings
    {
        public string Timezone { get; set; } = "America/New_York";
        public bool ExcludeOtc { get; set; } = true;
        public decimal MinPrice { get; set; } = 1m;
        public decimal MaxPrice { get; set; } = 100m;
        public long MinAvgVolume { get; set; } = 500_000;
        public decimal MinDollarVolume { get; set; } = 1_000_000m;
    }
}