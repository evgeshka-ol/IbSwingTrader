using IbSwingTrader.Models;

namespace IbSwingTrader.Extensions
{
    public static class TimeframeExtensions
    {
        public static string ToIBBarSize(this Timeframe tf)
        {
            return tf switch
            {
                Timeframe.M1 => "1 min",
                Timeframe.M5 => "5 mins",
                Timeframe.M15 => "15 mins",
                Timeframe.M30 => "30 mins",
                Timeframe.H1 => "1 hour",
                Timeframe.H4 => "4 hours",
                Timeframe.D1 => "1 day",
                Timeframe.W1 => "1 week",
                _ => throw new ArgumentOutOfRangeException(nameof(tf), tf, "Unsupported timeframe")
            };
        }

        public static string ToIBDuration(this Timeframe tf, int bars)
        {
            return tf switch
            {
                Timeframe.M1 => $"{bars * 60} S",
                Timeframe.M5 => $"{bars * 5 * 60} S",
                Timeframe.M15 => $"{bars * 15 * 60} S",
                Timeframe.M30 => $"{bars * 30 * 60} S",

                Timeframe.H1 => $"{Math.Max(1, bars / 24)} D",
                Timeframe.H4 => $"{Math.Max(1, bars / 6)} D",

                Timeframe.D1 => $"{bars} D",
                Timeframe.W1 => $"{bars} W",

                _ => throw new ArgumentOutOfRangeException(nameof(tf), tf, "Unsupported timeframe")
            };
        }

        public static TimeSpan ToTimeSpan(this Timeframe tf)
        {
            return tf switch
            {
                Timeframe.M1 => TimeSpan.FromMinutes(1),
                Timeframe.M5 => TimeSpan.FromMinutes(5),
                Timeframe.M15 => TimeSpan.FromMinutes(15),
                Timeframe.M30 => TimeSpan.FromMinutes(30),

                Timeframe.H1 => TimeSpan.FromHours(1),
                Timeframe.H4 => TimeSpan.FromHours(4),

                Timeframe.D1 => TimeSpan.FromDays(1),
                Timeframe.W1 => TimeSpan.FromDays(7),

                _ => throw new ArgumentOutOfRangeException(nameof(tf), tf, "Unsupported timeframe")
            };
        }

        public static TimeSpan GetMaxRequestSpan(this Timeframe tf)
        {
            return tf switch
            {
                Timeframe.M1 => TimeSpan.FromDays(1),
                Timeframe.M5 => TimeSpan.FromDays(1),
                Timeframe.M15 => TimeSpan.FromDays(1),
                Timeframe.M30 => TimeSpan.FromDays(1),
                Timeframe.H1 => TimeSpan.FromDays(1),

                Timeframe.H4 => TimeSpan.FromDays(5),

                Timeframe.D1 => TimeSpan.FromDays(30),
                Timeframe.W1 => TimeSpan.FromDays(180),

                _ => throw new ArgumentOutOfRangeException(nameof(tf), tf, "Unsupported timeframe")
            };
        }
    }
}