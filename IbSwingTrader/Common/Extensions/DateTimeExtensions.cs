using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Common.Extensions
{
    public static class DateTimeExtensions
    {
        public static string ToIbEndTime(this DateTime marketTime)
        {
            var utc = MarketTime.ToUtc(marketTime);

            return utc.ToString("yyyyMMdd HH:mm:ss 'UTC'");
        }

        public static string ToUaTimeFormat(this DateTime marketTime)
        {
            var normalized = MarketTime.Normalize(marketTime);

            return normalized.ToString("yyyy-MM-dd HH:mm:ss.fff 'ET'");

        }
    }
}
