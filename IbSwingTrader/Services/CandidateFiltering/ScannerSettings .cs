using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class ScannerSettings : IScannerSettings
    {
        public string LocationCode { get; set; } = "STK.US.MAJOR";

        public string ScanCode { get; set; } = "MOST_ACTIVE";

        public decimal MinPrice { get; set; } = 5m;
        public decimal MaxPrice { get; set; } = 200m;

        public decimal MinMarketCap { get; set; } = 1_000_000_000m;

        public long MinAvgVolume { get; set; } = 1_000_000;
    }
}
