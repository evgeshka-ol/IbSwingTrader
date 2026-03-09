namespace IbSwingTrader.Extensions
{
    public static class DateTimeExtensions
    {
        public static string ToIbEndTime(this DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                utc = utc.ToUniversalTime();

            return utc.ToString("yyyyMMdd HH:mm:ss 'UTC'");
        }
    }
}
