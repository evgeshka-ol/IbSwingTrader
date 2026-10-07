namespace IbSwingTrader.Application.Candidates
{
    public static class BellUpEntryTiming
    {
        private const decimal BurstBodyMultiplier = 2m;

        public static bool IsBoost(Candle candle, Candle preceding)
        {
            // Legacy body predicate retained for boost-body exit targets, not entry timing.
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
            out string reason,
            int rangeLookbackBars = 5,
            decimal rangeMultiplier = 2m)
            => IsReady(dailyCandles, h4Candles, patternTimeframe, scanTime, out reason, out _,
                rangeLookbackBars, rangeMultiplier);

        public static bool IsReady(
            IEnumerable<Candle> dailyCandles,
            IEnumerable<Candle> h4Candles,
            Timeframe patternTimeframe,
            DateTime scanTime,
            out string reason,
            out string shortReason,
            int rangeLookbackBars = 5,
            decimal rangeMultiplier = 2m)
        {
            if (patternTimeframe is not (Timeframe.D1 or Timeframe.H4))
            {
                shortReason = "Pattern unconfirmed";
                reason = "BellUp entry timing requires a confirmed Daily or H4 pattern";
                return false;
            }

            var isDaily = patternTimeframe == Timeframe.D1;
            if (rangeLookbackBars < 1 || rangeMultiplier <= 0m)
            {
                shortReason = "Invalid timing settings";
                reason = "BellUp range lookback and multiplier must be positive";
                return false;
            }
            var completed = GetCompletedCandles(isDaily ? dailyCandles : h4Candles, patternTimeframe, scanTime)
                .TakeLast(rangeLookbackBars + 1)
                .ToList();

            return IsTimeframeReady(completed, isDaily ? "Daily" : "H4",
                rangeLookbackBars, rangeMultiplier, out reason, out shortReason);
        }

        private static bool IsTimeframeReady(
            IReadOnlyList<Candle> completedCandles,
            string timeframe,
            int rangeLookbackBars,
            decimal rangeMultiplier,
            out string reason,
            out string shortReason)
        {
            shortReason = string.Empty;
            if (completedCandles.Count < rangeLookbackBars + 1)
            {
                shortReason = "Missing candles";
                reason = $"{timeframe}: {rangeLookbackBars + 1} completed candles are required for BellUp range timing";
                return false;
            }

            // Only the immediately preceding completed candle (T-1) blocks entry.
            // A one-day consolidation after a T-2 impulse is a valid continuation
            // setup, especially when H4 remains constructive.
            var candle = completedCandles[^1];
            var ranges = completedCandles.Take(completedCandles.Count - 1)
                .Select(x => x.High - x.Low).OrderBy(x => x).ToList();
            var middle = ranges.Count / 2;
            var median = ranges.Count % 2 == 0
                ? (ranges[middle - 1] + ranges[middle]) / 2m : ranges[middle];
            if (ranges[0] < 0m || median <= 0m || candle.High < candle.Low)
            {
                shortReason = "Missing range data";
                reason = $"{timeframe}: a positive median of valid candle ranges is required for BellUp timing";
                return false;
            }
            var range = candle.High - candle.Low;
            if (candle.Close > candle.Open && range >= rangeMultiplier * median)
            {
                shortReason = "Recent boost";
                reason = $"{timeframe}: BellUp boost on T-1 ({candle.Time:yyyy-MM-dd HH:mm:ss}); " +
                         $"green candle range={range}, PreviousMedianRange={median}, " +
                         $"RangeRatio={range / median:0.####}, Lookback={rangeLookbackBars}, " +
                         $"Threshold={rangeMultiplier}; do not enter";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
