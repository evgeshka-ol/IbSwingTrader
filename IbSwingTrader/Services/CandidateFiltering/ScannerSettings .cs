using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class ScannerSettings : IScannerSettings
    {
        public string LocationCode { get; set; } = "STK.US.MAJOR";

        // Было TOP_PERC_GAIN
        public string ScanCode { get; set; } = "TOP_PERC_LOSE";

        public double MinPrice { get; set; } = 5;
        public double MaxPrice { get; set; } = 200;

        // Пока market cap лучше не передавать в scanner filter,
        // он у тебя уже давал disabled.
        public double MinMarketCap { get; set; } = 1_000_000_000;

        public int MinAvgVolume { get; set; } = 1_000_000;
    }
}
