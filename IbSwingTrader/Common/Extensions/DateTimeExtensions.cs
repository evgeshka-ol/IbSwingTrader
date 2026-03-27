namespace IbSwingTrader.Common.Extensions
{
    public static class DateTimeExtensions
    {
        public static string ToIbEndTime(this DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                utc = utc.ToUniversalTime();

            return utc.ToString("yyyyMMdd HH:mm:ss 'UTC'");
        }

        public static string ToUaTimeFormat(this DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                utc = utc.ToUniversalTime();

            return utc.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");

        }
    }
}
