namespace IbSwingTrader.Common.Time
{
    public static class ElapsedTimeFormatter
    {
        public static string Format(TimeSpan elapsed)
        {
            return elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"m\:ss\.fff");
        }
    }
}
