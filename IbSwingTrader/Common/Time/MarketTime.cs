namespace IbSwingTrader.Common.Time
{
    public static class MarketTime
    {
        public const string DefaultTimeZoneId = "America/New_York";
        private const string WindowsEasternTimeZoneId = "Eastern Standard Time";

        public static DateTime Now(string? timeZoneId = null)
        {
            return FromUtc(DateTime.UtcNow, timeZoneId);
        }

        public static DateTime FromUtc(DateTime utc, string? timeZoneId = null)
        {
            if (utc.Kind != DateTimeKind.Utc)
                utc = utc.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
                    : utc.ToUniversalTime();

            return TimeZoneInfo.ConvertTimeFromUtc(utc, GetTimeZone(timeZoneId));
        }

        public static DateTime ToUtc(DateTime marketTime, string? timeZoneId = null)
        {
            if (marketTime.Kind == DateTimeKind.Utc)
                return marketTime;

            if (marketTime.Kind == DateTimeKind.Local)
                return marketTime.ToUniversalTime();

            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(marketTime, DateTimeKind.Unspecified),
                GetTimeZone(timeZoneId));
        }

        public static DateTime Normalize(DateTime value, string? timeZoneId = null)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => FromUtc(value, timeZoneId),
                DateTimeKind.Local => FromUtc(value.ToUniversalTime(), timeZoneId),
                _ => value
            };
        }

        public static TimeZoneInfo GetTimeZone(string? timeZoneId = null)
        {
            var requestedId = string.IsNullOrWhiteSpace(timeZoneId)
                ? DefaultTimeZoneId
                : timeZoneId;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(requestedId);
            }
            catch (TimeZoneNotFoundException) when (
                string.Equals(requestedId, DefaultTimeZoneId, StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(WindowsEasternTimeZoneId);
            }
            catch (TimeZoneNotFoundException) when (
                string.Equals(requestedId, WindowsEasternTimeZoneId, StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
            }
        }
    }
}
