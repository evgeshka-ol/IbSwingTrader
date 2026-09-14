namespace IbSwingTrader.Application.Candidates
{
    public static class BellUpLateEntryPenalty
    {
        public static decimal Calculate(
            IReadOnlyList<decimal> open,
            IReadOnlyList<decimal> high,
            IReadOnlyList<decimal> low,
            IReadOnlyList<decimal> close)
        {
            var count = Math.Min(Math.Min(open.Count, high.Count), Math.Min(low.Count, close.Count));
            if (count < 4)
                return 0m;

            var currentBody = close[^1] - open[^1];
            var range = high[^1] - low[^1];
            if (currentBody <= 0m || range <= 0m || close[^1] < low[^1] + range * 0.70m)
                return 0m;

            var boosts = new List<decimal>();
            for (var i = count - 2; i >= 1 && boosts.Count < 2; i--)
            {
                var body = close[i] - open[i];
                var precedingBody = Math.Abs(close[i - 1] - open[i - 1]);
                if (body > 0m && body >= 2m * precedingBody)
                    boosts.Add(body);
            }

            if (boosts.Count == 0)
                return 0m;

            var reference = boosts.Count == 1 ? boosts[0] : (boosts[0] + boosts[1]) / 2m;
            if (reference <= 0m)
                return 0m;

            var ratio = currentBody / reference;
            if (ratio < 0.35m || ratio > 2.50m)
                return 0m;

            // Ranking-quality points, not percentage points. Two prior boosts provide stronger evidence.
            return boosts.Count == 2 ? 1.50m : 0.75m;
        }
    }
}
