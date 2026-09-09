namespace IbSwingTrader.Application.Candidates
{
    internal static class BellUpEntryTiming
    {
        private const decimal BurstBodyMultiplier = 2m;

        public static bool IsReady(
            IEnumerable<Candle> dailyCandles,
            IEnumerable<Candle> h4Candles,
            DateTime scanTime,
            out string reason)
        {
            var completedDaily = dailyCandles
                .Where(x => x.Time.Date.AddHours(16) <= scanTime)
                .OrderBy(x => x.Time)
                .TakeLast(3)
                .ToList();
            var completedH4 = h4Candles
                .Where(x => x.Time.AddHours(4) <= scanTime)
                .OrderBy(x => x.Time)
                .TakeLast(3)
                .ToList();

            return IsTimeframeReady(completedDaily, "Daily", out reason) &&
                   IsTimeframeReady(completedH4, "H4", out reason);
        }

        private static bool IsTimeframeReady(
            IReadOnlyList<Candle> completedCandles,
            string timeframe,
            out string reason)
        {
            if (completedCandles.Count < 3)
            {
                reason = $"{timeframe}: three completed candles are required for BellUp entry timing";
                return false;
            }

            // Check T-1 first, then T-2; each green body is compared with its predecessor.
            for (var offset = 1; offset <= 2; offset++)
            {
                var candle = completedCandles[completedCandles.Count - offset];
                var preceding = completedCandles[completedCandles.Count - offset - 1];
                var body = candle.Close - candle.Open;
                var precedingLength = Math.Abs(preceding.Close - preceding.Open);
                if (body > 0m && body >= BurstBodyMultiplier * precedingLength)
                {
                    reason = $"{timeframe}: BellUp boost on T-{offset} ({candle.Time:yyyy-MM-dd HH:mm:ss}); " +
                             "green body is at least 2x the preceding body; do not enter";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }
    }
}
