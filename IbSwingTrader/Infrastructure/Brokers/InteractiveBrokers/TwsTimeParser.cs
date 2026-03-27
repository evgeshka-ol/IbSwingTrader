using System.Globalization;

namespace IbSwingTrader.Infrastructure.Brokers.InteractiveBrokers
{
    public static class TwsTimeParser
    {
        public static DateTime ParseToUtc(string ibTime)
        {
            var parts = ibTime.Split(' ');

            // DAILY BAR
            if (parts.Length == 1)
            {
                var date = DateTime.ParseExact(
                    parts[0],
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal);

                return date;
            }

            // INTRADAY BAR
            var localTime = DateTime.ParseExact(
                $"{parts[0]} {parts[1]}",
                "yyyyMMdd HH:mm:ss",
                CultureInfo.InvariantCulture);

            var tz = ParseIbTimezone(parts[2]);

            return TimeZoneInfo.ConvertTimeToUtc(localTime, tz);
        }

        private static TimeZoneInfo ParseIbTimezone(string tz)
        {
            return tz switch
            {
                "US/Eastern" => TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"),
                "UTC" => TimeZoneInfo.Utc,
                _ => TimeZoneInfo.Utc
            };
        }
    }
}
