namespace IbSwingTrader.Application.Candidates
{
    public static class BellUpEntryTiming
    {
        private const decimal BurstBodyMultiplier = 2m;

        public static bool IsBoost(Candle candle, Candle preceding)
        {
            var body = candle.Close - candle.Open;
            return body > 0m && body >= BurstBodyMultiplier * Math.Abs(preceding.Close - preceding.Open);
        }

        public static List<Candle> GetCompletedCandles(IEnumerable<Candle> candles, Timeframe timeframe, DateTime scanTime)
        {
            if (timeframe is not (Timeframe.D1 or Timeframe.H4))
                return [];

            return candles
                .Where(x => (timeframe == Timeframe.D1 ? x.Time.Date.AddHours(16) : x.Time.AddHours(4)) <= scanTime)
                .OrderBy(x => x.Time)
                .ToList();
        }

        public static bool IsReady(
            IEnumerable<Candle> dailyCandles,
            IEnumerable<Candle> h4Candles,
            Timeframe patternTimeframe,
            DateTime scanTime,
            out string reason)
            => IsReady(dailyCandles, h4Candles, patternTimeframe, scanTime, out reason, out _);

        public static bool IsReady(
            IEnumerable<Candle> dailyCandles,
            IEnumerable<Candle> h4Candles,
            Timeframe patternTimeframe,
            DateTime scanTime,
            out string reason,
            out string shortReason)
        {
            if (patternTimeframe is not (Timeframe.D1 or Timeframe.H4))
            {
                shortReason = "Pattern unconfirmed";
                reason = "BellUp entry timing requires a confirmed Daily or H4 pattern";
                return false;
            }

            var isDaily = patternTimeframe == Timeframe.D1;
            var completed = GetCompletedCandles(isDaily ? dailyCandles : h4Candles, patternTimeframe, scanTime)
                .TakeLast(3)
                .ToList();

            return IsTimeframeReady(completed, isDaily ? "Daily" : "H4", out reason, out shortReason);
        }

        private static bool IsTimeframeReady(
            IReadOnlyList<Candle> completedCandles,
            string timeframe,
            out string reason,
            out string shortReason)
        {
            shortReason = string.Empty;
            if (completedCandles.Count < 3)
            {
                shortReason = "Missing candles";
                reason = $"{timeframe}: three completed candles are required for BellUp entry timing";
                return false;
            }

            // Only the immediately preceding completed candle (T-1) blocks entry.
            // A one-day consolidation after a T-2 impulse is a valid continuation
            // setup, especially when H4 remains constructive.
            var candle = completedCandles[^1];
            var preceding = completedCandles[^2];
            if (IsBoost(candle, preceding))
            {
                shortReason = "Recent boost";
                reason = $"{timeframe}: BellUp boost on T-1 ({candle.Time:yyyy-MM-dd HH:mm:ss}); " +
                         "green body is at least 2x the preceding body; do not enter";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
