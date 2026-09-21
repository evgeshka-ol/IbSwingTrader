namespace IbSwingTrader.Domain.Candidates
{
    public class ScanInfo
    {
        public string PresetScanCode { get; set; } = string.Empty;

        public string PresetDescription { get; set; } = string.Empty;

        public DateTime ScanTime { get; set; }

        public DateTime? RunStartedAt { get; set; }
        public DateTime? FirstSeenAt { get; set; }
        public DateTime? SignalObservedAt { get; set; }
        public DateTime? PublishedAt { get; set; }
        public decimal? DetectionToPublicationSeconds { get; set; }
        public decimal? PriceToPublicationSeconds { get; set; }

        public string ScanTimeZone { get; set; } = string.Empty;
    }
}
