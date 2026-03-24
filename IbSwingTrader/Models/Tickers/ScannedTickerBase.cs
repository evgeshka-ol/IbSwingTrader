namespace IbSwingTrader.Models.Tickers
{
    public abstract class ScannedTickerBase : TickerEntity
    {
        public string PresetScanCode { get; set; } = string.Empty;

        public string PresetDescription { get; set; } = string.Empty;

        public DateTime ScanTimeMarket { get; set; }

        public string ScanTimeZone { get; set; } = string.Empty;

        public decimal Score { get; set; }

        public decimal? WeeklyScore { get; set; }

        public decimal? DailyScore { get; set; }

        public decimal? EntryScore { get; set; }

        public decimal DistanceTo20dHigh { get; set; }

        public decimal DistanceTo52wHigh { get; set; }

        public decimal DailyRSI14 { get; set; }

        public string? Notes { get; set; }
    }
}