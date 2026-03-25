namespace IbSwingTrader.Models
{
    public class MarketSessionSchedule
    {
        public MarketScheduleSource Source { get; set; } = MarketScheduleSource.LocalConfig;
        public string TimeZoneId { get; set; } = "America/New_York";
        public DateTime LoadedAtUtc { get; set; }

        public List<TradingDaySchedule> Days { get; set; } = [];
    }

    public class TradingDaySchedule
    {
        public DateOnly Date { get; set; }
        public bool IsTradingDay { get; set; }
        public bool IsHoliday { get; set; }
        public bool IsEarlyClose { get; set; }

        public List<SessionInterval> Sessions { get; set; } = [];
    }

    public class SessionInterval
    {
        public MarketSessionType Type { get; set; } = MarketSessionType.Unknown;
        public MarketSessionScope Scope { get; set; } = MarketSessionScope.Unknown;
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
    }
}