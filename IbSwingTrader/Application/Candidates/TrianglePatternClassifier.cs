namespace IbSwingTrader.Application.Candidates;

internal static class TrianglePatternClassifier
{
    public static bool IsPostSpikeConsolidation(
        IReadOnlyList<decimal> opens,
        IReadOnlyList<decimal> closes,
        IReadOnlyList<decimal>? upper = null,
        IReadOnlyList<decimal>? mid = null,
        IReadOnlyList<decimal>? lower = null)
    {
        if (opens.Count != closes.Count || opens.Count < 5)
            return false;

        // First retain the original, very specific one-candle ACVA rule.
        for (var spike = opens.Count - 2; spike >= 1; spike--)
        {
            var spikeBody = closes[spike] - opens[spike];
            var previousBody = Math.Abs(closes[spike - 1] - opens[spike - 1]);
            if (spikeBody <= 0m || spikeBody < previousBody * 2m)
                continue;

            // Never explain the current state with an older impulse when the
            // newest qualifying impulse has too little follow-up data.
            if (closes.Count - spike - 1 < 2)
            {
                if (upper is null)
                    return false;
                break;
            }

            var reference = Math.Abs(closes[spike]);
            var maxBody = Math.Max(spikeBody * 0.25m, reference * 0.003m);
            var maxCloseSpread = Math.Max(spikeBody * 0.50m, reference * 0.005m);
            var minClose = closes[spike + 1];
            var maxClose = minClose;
            var plateauValid = true;

            for (var i = spike + 1; i < closes.Count; i++)
            {
                if (Math.Abs(closes[i] - opens[i]) > maxBody ||
                    Math.Abs(closes[i] - closes[spike]) > maxCloseSpread)
                {
                    if (upper is null)
                        return false;
                    plateauValid = false;
                    break;
                }

                minClose = Math.Min(minClose, closes[i]);
                maxClose = Math.Max(maxClose, closes[i]);
                if (maxClose - minClose > maxCloseSpread)
                {
                    if (upper is null)
                        return false;
                    plateauValid = false;
                    break;
                }
            }

            if (plateauValid)
                return true;
        }

        // A Triangle may be formed by a short sequence of rising candles. The
        // envelope check prevents an active BellUp continuation from being
        // relabelled while its latest expansion is still developing.
        if (upper is null || mid is null || lower is null ||
            upper.Count != closes.Count || mid.Count != closes.Count || lower.Count != closes.Count)
            return false;

        for (var rampEnd = closes.Count - 6; rampEnd >= 2; rampEnd--)
        {
            var rampStart = Math.Max(0, rampEnd - 5);
            var gain = closes[rampEnd] - closes[rampStart];
            var reference = Math.Abs(closes[rampStart]);
            if (gain <= 0m || gain < reference * 0.05m)
                continue;

            var rampBodies = Enumerable.Range(rampStart + 1, rampEnd - rampStart)
                .Select(i => Math.Abs(closes[i] - opens[i]))
                .ToList();
            if (rampBodies.Count == 0 || rampBodies.Average() <= 0m)
                continue;

            var tailStart = rampEnd + 1;
            var tailLength = closes.Count - tailStart;
            if (tailLength < 6)
                continue;

            var tail = closes.Skip(tailStart).ToList();
            var tailBodies = Enumerable.Range(tailStart, tailLength)
                .Select(i => Math.Abs(closes[i] - opens[i]))
                .OrderBy(x => x)
                .ToList();
            var medianBody = tailBodies[tailBodies.Count / 2];
            var spread = tail.Max() - tail.Min();
            var drift = Math.Abs(tail.Take(3).Average() - tail.TakeLast(3).Average());
            if (medianBody > rampBodies.Average() * 0.55m ||
                spread > gain * 0.60m ||
                drift > gain * 0.35m)
                continue;

            var widths = Enumerable.Range(rampStart, closes.Count - rampStart)
                .Select(i => upper[i] - lower[i])
                .ToList();
            var peakWidth = widths.Max();
            var finalWidth = widths[^1];
            var peakIndex = rampStart + widths.IndexOf(peakWidth);
            if (peakIndex < rampEnd || finalWidth > peakWidth * 0.90m)
                continue;

            if (upper[^1] >= upper[peakIndex] || lower[^1] <= lower[peakIndex])
                continue;

            return true;
        }

        return false;
    }
}
