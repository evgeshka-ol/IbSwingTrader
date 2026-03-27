namespace IbSwingTrader.Domain.Settings
{
    public class MarketSessionsSettings
    {
        public bool UseExtendedHoursByDefault { get; set; } = true;

        public bool EnableHolidaySupport { get; set; } = false;

        public bool EnableEarlyCloseSupport { get; set; } = false;

        public List<DayOfWeek> TradingDays { get; set; } =
        [
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday
        ];

        public SessionWindowSettings PreMarket { get; set; } = new()
        {
            Enabled = true,
            Start = "04:00",
            End = "09:30"
        };

        public SessionWindowSettings RegularSession { get; set; } = new()
        {
            Enabled = true,
            Start = "09:30",
            End = "16:00"
        };

        public SessionWindowSettings AfterHours { get; set; } = new()
        {
            Enabled = true,
            Start = "16:00",
            End = "20:00"
        };

        public TwsOverridesSettings TwsOverrides { get; set; } = new();

        public List<MarketHolidaySettings> Holidays { get; set; } = [];

        public List<EarlyCloseSettings> EarlyCloses { get; set; } = [];
    }

    public class SessionWindowSettings
    {
        public bool Enabled { get; set; } = true;

        public string Start { get; set; } = string.Empty;

        public string End { get; set; } = string.Empty;
    }

    public class TwsOverridesSettings
    {
        public bool Enabled { get; set; } = true;

        public bool PreferTwsTradingHours { get; set; } = true;

        public bool PreferTwsLiquidHours { get; set; } = false;

        public int CacheTtlHours { get; set; } = 24;
    }

    public class MarketHolidaySettings
    {
        public string Date { get; set; } = string.Empty;

        public string? Name { get; set; }
    }

    public class EarlyCloseSettings
    {
        public string Date { get; set; } = string.Empty;

        public string CloseTime { get; set; } = string.Empty;

        public string? Name { get; set; }
    }
}