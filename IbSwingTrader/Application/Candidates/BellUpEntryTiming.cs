namespace IbSwingTrader.Application.Candidates
{
    public sealed record BellUpTimingObservation(Candle Bar, DateTime DecisionAt, DateTime CoveredThrough);

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
            decimal rangeMultiplier = 2m, BellUpTimingObservation? current = null)
            => IsReady(dailyCandles, h4Candles, patternTimeframe, scanTime, out reason, out _,
                rangeLookbackBars, rangeMultiplier, current);

        public static bool IsReady(
            IEnumerable<Candle> dailyCandles,
            IEnumerable<Candle> h4Candles,
            Timeframe patternTimeframe,
            DateTime scanTime,
            out string reason,
            out string shortReason,
            int rangeLookbackBars = 5,
            decimal rangeMultiplier = 2m, BellUpTimingObservation? current = null)
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
            var completed = GetCompletedCandles(isDaily ? dailyCandles : h4Candles, patternTimeframe, scanTime);
            if (current != null)
            {
                var start = CurrentStart(patternTimeframe, scanTime);
                if (start == null || current.Bar.Time != start.Value || current.Bar.Timeframe != patternTimeframe ||
                    current.DecisionAt != scanTime || current.CoveredThrough !=
                        scanTime.AddTicks(-(scanTime.Ticks % TimeSpan.FromMinutes(5).Ticks)) ||
                    current.CoveredThrough <= start.Value || current.Bar.Volume <= 0m || !Valid(current.Bar))
                {
                    shortReason = "Invalid current candle";
                    reason = "BellUp current timing candle must be a matching as-of M5 observation";
                    return false;
                }
            }
            var observed = current == null ? completed.TakeLast(rangeLookbackBars + 1).ToList()
                : completed.Where(x => x.Time < current.Bar.Time).TakeLast(rangeLookbackBars).Append(current.Bar).ToList();
            var ready = IsTimeframeReady(observed, isDaily ? "Daily" : "H4",
                rangeLookbackBars, rangeMultiplier, out reason, out shortReason);
            if (current != null)
                reason = FormattableString.Invariant(
                    $"BellUpTiming(State=Forming, Timeframe={patternTimeframe}, BarTime={current.Bar.Time:O}, DecisionAt={current.DecisionAt:O}, CoveredThrough={current.CoveredThrough:O}, Source=CompletedM5, Open={current.Bar.Open}, High={current.Bar.High}, Low={current.Bar.Low}, Close={current.Bar.Close}, Ready={ready}); {reason}");
            return ready;
        }

        public static DateTime? CurrentStart(Timeframe timeframe, DateTime decisionAt)
        {
            if (timeframe == Timeframe.H4)
            {
                var start = decisionAt.AddTicks(-(decisionAt.Ticks % TimeSpan.FromHours(4).Ticks));
                return start.Hour is 4 or 8 or 12 or 16 ? start : null;
            }
            // Native Daily is the regular session; pre/aftermarket belongs to H4 only.
            return timeframe == Timeframe.D1 && decisionAt.TimeOfDay >= TimeSpan.FromHours(9.5) &&
                decisionAt.TimeOfDay < TimeSpan.FromHours(16) ? decisionAt.Date.AddHours(9.5) : null;
        }

        public static BellUpTimingObservation? BuildCurrentFromM5(IEnumerable<Candle> m5,
            Timeframe timeframe, DateTime decisionAt)
        {
            var start = CurrentStart(timeframe, decisionAt);
            if (!start.HasValue)
                return null;
            var through = decisionAt.AddTicks(-(decisionAt.Ticks % TimeSpan.FromMinutes(5).Ticks));
            // Final values of a forming cached M5/H4 cannot enter a historical prefix.
            var bars = m5.Where(x => x.Time >= start.Value && x.Time.AddMinutes(5) <= through)
                .GroupBy(x => x.Time).Select(x => x.Last()).OrderBy(x => x.Time).ToList();
            if (bars.Count == 0 || bars[0].Time != start.Value || bars[^1].Time.AddMinutes(5) != through ||
                bars.Any(x => !Valid(x) || x.Volume < 0m) ||
                bars.Zip(bars.Skip(1), (a,b) => b.Time - a.Time != TimeSpan.FromMinutes(5)).Any(x => x))
                return null;
            var traded = bars.Where(x => x.Volume > 0m).ToList();
            if (traded.Count == 0)
                return null;
            return new(new Candle { Timeframe = timeframe, Time = start.Value,
                Open = traded[0].Open, High = traded.Max(x => x.High), Low = traded.Min(x => x.Low),
                Close = traded[^1].Close, Volume = traded.Sum(x => x.Volume) }, decisionAt, through);
        }

        private static bool Valid(Candle bar) => bar.Open > 0m && bar.Close > 0m && bar.Low > 0m &&
            bar.High >= Math.Max(bar.Open, bar.Close) && bar.Low <= Math.Min(bar.Open, bar.Close);

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
                reason = $"{timeframe}: {rangeLookbackBars} preceding closed candles and the latest observation are required for BellUp range timing";
                return false;
            }

            // Inspect the latest observed candle, including an explicit as-of forming
            // candle when available. The same range threshold applies to both.
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
                reason = $"{timeframe}: BellUp boost on latest observed candle ({candle.Time:yyyy-MM-dd HH:mm:ss}); " +
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
