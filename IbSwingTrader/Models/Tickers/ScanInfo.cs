namespace IbSwingTrader.Models.Tickers
{
    public class ScanInfo
    {
        public string PresetScanCode { get; set; } = string.Empty;

        public string PresetDescription { get; set; } = string.Empty;

        public DateTime ScanTimeMarket { get; set; }

        public string ScanTimeZone { get; set; } = string.Empty;
    }
}