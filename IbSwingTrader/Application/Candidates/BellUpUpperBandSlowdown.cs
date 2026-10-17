namespace IbSwingTrader.Application.Candidates
{
    // Diagnostic pause geometry only; it does not predict a fall or veto entry.
    public static class BellUpUpperBandSlowdown
    {
        public static bool IsPause(
            IReadOnlyList<Candle> completed,
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid)
        {
            var count = completed.Count;
            if (count < 4 || upper.Count != count || mid.Count != count)
                return false;

            for (var i = count - 4; i < count; i++)
            {
                if (completed[i].Close <= 0m || mid[i] <= 0m || upper[i] <= mid[i] ||
                    (i > count - 4 && completed[i].Time <= completed[i - 1].Time))
                    return false;
            }

            var first = upper[^3] - upper[^4];
            var previous = upper[^2] - upper[^3];
            var latest = upper[^1] - upper[^2];
            return first > 0m && previous > 0m && previous < first && latest < previous &&
                   // The rise has stalled above a still-rising/flat middle band.
                   Math.Max(completed[^3].Close, completed[^2].Close) > completed[^4].Close &&
                   completed[^1].Close <= completed[^2].Close &&
                   completed[^1].Close > mid[^1] && mid[^1] >= mid[^2];
        }
    }
}
