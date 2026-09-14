namespace IbSwingTrader.Application.Candidates
{
    public readonly record struct BellUpBoostExitTarget(
        decimal ExitPrice, Candle LatestBoost, Candle? EarlierBoost, decimal AverageBody, int BoostCount);

    public static class BellUpBoostExit
    {
        // Candles and classification flags are aligned, completed and oldest-first.
        public static bool TryCalculate(
            IReadOnlyList<Candle> completed,
            IReadOnlyList<bool> bellUpAtCandle,
            decimal entry,
            out BellUpBoostExitTarget target,
            out string reason)
        {
            target = default;
            reason = "No completed boost in the current confirmed BellUp episode";
            if (entry <= 0m || completed.Count < 3 || completed.Count != bellUpAtCandle.Count)
                return false;

            Candle? latestBoost = null;
            for (var i = completed.Count - 1; i >= 1; i--)
            {
                if (!bellUpAtCandle[i])
                    break;

                if (!BellUpEntryTiming.IsBoost(completed[i], completed[i - 1]))
                    continue;

                if (latestBoost == null)
                {
                    latestBoost = completed[i];
                    continue;
                }

                var earlierBoost = completed[i];
                var average = ((latestBoost.Close - latestBoost.Open) +
                               (earlierBoost.Close - earlierBoost.Open)) / 2m;
                var exit = decimal.Round(entry + average, 2, MidpointRounding.AwayFromZero);
                if (exit <= entry)
                {
                    reason = "Average boost body does not produce a positive target after price rounding";
                    return false;
                }

                target = new BellUpBoostExitTarget(exit, latestBoost, earlierBoost, average, 2);
                reason = string.Empty;
                return true;
            }

            if (latestBoost != null)
            {
                var body = latestBoost.Close - latestBoost.Open;
                var exit = decimal.Round(entry + body, 2, MidpointRounding.AwayFromZero);
                if (exit <= entry)
                {
                    reason = "Boost body does not produce a positive target after price rounding";
                    return false;
                }

                target = new BellUpBoostExitTarget(exit, latestBoost, null, body, 1);
                reason = string.Empty;
                return true;
            }

            return false;
        }
    }
}
