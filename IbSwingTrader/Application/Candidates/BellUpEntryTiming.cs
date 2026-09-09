namespace IbSwingTrader.Application.Candidates
{
    public static class BellUpEntryTiming
    {
        private const decimal BurstBodyMultiplier = 2m;

        public static bool IsReady(
            IEnumerable<Candle> dailyCandles,
            IEnumerable<Candle> h4Candles,
            Timeframe patternTimeframe,
            DateTime scanTime,
            out string reason)
        {
            if (patternTimeframe is not (Timeframe.D1 or Timeframe.H4))
            {
                reason = "BellUp entry timing requires a confirmed Daily or H4 pattern";
                return false;
            }

            var isDaily = patternTimeframe == Timeframe.D1;
            var completed = (isDaily ? dailyCandles : h4Candles)
                .Where(x => (isDaily ? x.Time.Date.AddHours(16) : x.Time.AddHours(4)) <= scanTime)
                .OrderBy(x => x.Time)
                .TakeLast(3)
                .ToList();

            return IsTimeframeReady(completed, isDaily ? "Daily" : "H4", out reason);
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
