using System.Globalization;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Infrastructure.Brokers.InteractiveBrokers
{
    public static class TwsTimeParser
    {
        public static DateTime ParseMarketTime(string ibTime)
        {
            var parts = ibTime.Split(' ');

            // DAILY BAR
            if (parts.Length == 1)
            {
                return DateTime.ParseExact(
                    parts[0],
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None);
            }

            // INTRADAY BAR
            var parsedTime = DateTime.ParseExact(
                $"{parts[0]} {parts[1]}",
                "yyyyMMdd HH:mm:ss",
                CultureInfo.InvariantCulture);

            var tz = ParseIbTimezone(parts[2]);
            var marketTimeZone = MarketTime.GetTimeZone();

            if (tz.Id == marketTimeZone.Id)
                return parsedTime;

            var utc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(parsedTime, DateTimeKind.Unspecified),
                tz);

            return MarketTime.FromUtc(utc);
        }

        private static TimeZoneInfo ParseIbTimezone(string tz)
        {
            return tz switch
            {
                "US/Eastern" => MarketTime.GetTimeZone(),
                "UTC" => TimeZoneInfo.Utc,
                _ => MarketTime.GetTimeZone()
            };
        }
    }
}
