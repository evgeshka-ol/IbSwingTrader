using System.Globalization;

namespace IbSwingTrader.Application.Candidates
{
    // Observations only. Neither a new recognition seed nor an entry condition.
    public static class ReversalLowerBandBodyDiagnostics
    {
        public static string Describe(IReadOnlyList<Candle> candles, IReadOnlyList<decimal> lower,
            Timeframe timeframe, DateTime decisionAt)
        {
            var prefix = FormattableString.Invariant(
                $"ReversalLowerBandBody(DiagnosticOnly=True, Timeframe={timeframe}, DecisionAt={decisionAt:O}");
            if (candles.Count != lower.Count || timeframe is not (Timeframe.H4 or Timeframe.D1))
                return prefix + ", Status=InvalidInput)";

            var closed = Enumerable.Range(0, candles.Count).Where(i =>
                (timeframe == Timeframe.D1 ? candles[i].Time.Date.AddHours(16) : candles[i].Time.AddHours(4)) <= decisionAt)
                .ToList();
            if (closed.Count == 0)
                return prefix + ", Status=NoCompletedBars)";
            for (var j = 0; j < closed.Count; j++)
            {
                var b = candles[closed[j]];
                if (b.Open <= 0m || b.Close <= 0m || b.Low <= 0m ||
                    b.Low > Math.Min(b.Open, b.Close) || b.High < Math.Max(b.Open, b.Close) ||
                    (j > 0 && b.Time <= candles[closed[j - 1]].Time))
                    return prefix + ", Status=InvalidCandles)";
            }

            static decimal Median(IEnumerable<decimal> values)
            {
                var ordered = values.Order().ToList();
                return ordered.Count == 0 ? 0m : ordered.Count % 2 == 1
                    ? ordered[ordered.Count / 2] : (ordered[ordered.Count / 2 - 1] + ordered[ordered.Count / 2]) / 2m;
            }
            static string Ratio(decimal value, decimal median) => median > 0m
                ? (value / median).ToString(CultureInfo.InvariantCulture) : "n/a";
            string Position(int j)
            {
                var i = closed[j];
                if (lower[i] <= 0m)
                    return "Unavailable";
                return lower[i] > Math.Max(candles[i].Open, candles[i].Close) ? "AboveBody"
                    : lower[i] < Math.Min(candles[i].Open, candles[i].Close) ? "BelowBody" : "InsideBody";
            }

            var observations = new List<string>();
            for (var j = Math.Max(0, closed.Count - 4); j < closed.Count; j++)
            {
                var i = closed[j];
                var b = candles[i];
                var baseline = closed.Take(j).TakeLast(5).Select(k => candles[k]).ToList();
                var bodyMedian = Median(baseline.Select(x => Math.Abs(x.Close - x.Open)));
                var rangeMedian = Median(baseline.Select(x => x.High - x.Low));
                observations.Add(FormattableString.Invariant(
                    $"Bar(Time={b.Time:O}, Open={b.Open}, High={b.High}, Low={b.Low}, Close={b.Close}, Volume={b.Volume}, Lower={lower[i]}, LowerVsBody={Position(j)}, BodyPct={(b.Close - b.Open) / b.Open * 100m}, BodyToPriorMedian={Ratio(Math.Abs(b.Close - b.Open), bodyMedian)}, RangeToPriorMedian={Ratio(b.High - b.Low, rangeMedian)}, ReferenceBars={baseline.Count})"));
            }
            var lastPosition = Position(closed.Count - 1);
            var previousPosition = closed.Count > 1 ? Position(closed.Count - 2) : "Unavailable";
            return prefix + $", Status=Observed, LastLowerVsBody={lastPosition}, " +
                $"PreviousLowerVsBody={previousPosition}, " +
                $"PositionChangedIntoBodyFromAbove={previousPosition == "AboveBody" && lastPosition == "InsideBody"}, " +
                string.Join("; ", observations) + ")";
        }
    }
}
