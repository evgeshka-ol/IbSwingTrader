namespace IbSwingTrader.Application.Candidates;

internal static class TrianglePatternClassifier
{
    public static bool IsPostSpikeConsolidation(
        IReadOnlyList<decimal> opens, IReadOnlyList<decimal> closes)
    {
        if (opens.Count != closes.Count || opens.Count < 5)
            return false;

        for (var spike = opens.Count - 1; spike >= 1; spike--)
        {
            var spikeBody = closes[spike] - opens[spike];
            var previousBody = Math.Abs(closes[spike - 1] - opens[spike - 1]);
            if (spikeBody <= 0m || spikeBody < previousBody * 2m)
                continue;

            // Only the latest impulse can anchor the current plateau.
            if (closes.Count - spike - 1 < 2)
                return false;

            var reference = Math.Abs(closes[spike]);
            var maxBody = Math.Max(spikeBody * 0.25m, reference * 0.003m);
            var maxCloseSpread = Math.Max(spikeBody * 0.50m, reference * 0.005m);
            var minClose = closes[spike + 1];
            var maxClose = minClose;

            for (var i = spike + 1; i < closes.Count; i++)
            {
                if (Math.Abs(closes[i] - opens[i]) > maxBody ||
                    Math.Abs(closes[i] - closes[spike]) > maxCloseSpread)
                    return false;

                minClose = Math.Min(minClose, closes[i]);
                maxClose = Math.Max(maxClose, closes[i]);
                if (maxClose - minClose > maxCloseSpread)
                    return false;
            }

            return true;
        }

        return false;
    }
}
