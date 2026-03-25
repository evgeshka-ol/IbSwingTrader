namespace IbSwingTrader.Models.Settings
{
    public class MarketSessionsSettings
    {
        public string TimeZoneId { get; set; } = "America/New_York";

        public SessionWindowSettings? PreMarket { get; set; } = new();
        public SessionWindowSettings? RegularSession { get; set; } = new();
        public SessionWindowSettings? AfterHours { get; set; } = new();

        public List<DayOfWeek> TradingDays { get; set; } =
        [
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday
        ];

        public bool UseExtendedHoursByDefault { get; set; } = true;

        public bool EnableHolidaySupport { get; set; } = false;
        public bool EnableEarlyCloseSupport { get; set; } = false;

        public List<MarketHolidaySettings> Holidays { get; set; } = [];
        public List<EarlyCloseSettings> EarlyCloses { get; set; } = [];
    }

    public class SessionWindowSettings
    {
        public bool Enabled { get; set; } = true;

        // "04:00", "09:30", "16:00", "20:00"
        public string Start { get; set; } = string.Empty;
        public string End { get; set; } = string.Empty;
    }

    public class MarketHolidaySettings
    {
        // "2026-01-19"
        public string Date { get; set; } = string.Empty;
        public string? Name { get; set; }
    }

    public class EarlyCloseSettings
    {
        // "2026-11-27"
        public string Date { get; set; } = string.Empty;
        public string CloseTime { get; set; } = string.Empty; // "13:00"
        public string? Name { get; set; }
    }
}