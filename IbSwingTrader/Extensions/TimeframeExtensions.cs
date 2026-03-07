using IbSwingTrader.Models;

namespace IbSwingTrader.Extensions;

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
        if (bars <= 0)
            throw new ArgumentException("Bars must be greater than zero", nameof(bars));

        return tf switch
        {
            Timeframe.M1 => $"{bars} M",
            Timeframe.M5 => $"{bars * 5} M",
            Timeframe.M15 => $"{bars * 15} M",
            Timeframe.M30 => $"{bars * 30} M",

            Timeframe.H1 => $"{bars} H",
            Timeframe.H4 => $"{bars * 4} H",

            Timeframe.D1 => $"{bars} D",
            Timeframe.W1 => $"{bars} W",

            _ => throw new ArgumentOutOfRangeException(nameof(tf), tf, "Unsupported timeframe")
        };
    }
}