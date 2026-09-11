namespace IbSwingTrader.Application.Candidates
{
    public static class BellUpLowerBandTurn
    {
        public static bool TryFindConfirmedTurn(
            IReadOnlyList<decimal> lower,
            IReadOnlyList<bool> bellUpAtCandle,
            out int troughIndex)
        {
            troughIndex = -1;
            if (lower.Count < 4 || lower.Count != bellUpAtCandle.Count || !bellUpAtCandle[^1])
                return false;

            var start = lower.Count - 1;
            while (start > 0 && bellUpAtCandle[start - 1])
                start--;

            var minimumIndex = start;
            for (var i = start; i < lower.Count; i++)
            {
                if (lower[i] <= 0m)
                    return false;
                // Use the final point of a flat minimum, not the first rounded equal value.
                if (lower[i] <= lower[minimumIndex])
                    minimumIndex = i;
            }

            if (minimumIndex > lower.Count - 3 ||
                lower[^2] <= lower[minimumIndex] || lower[^1] <= lower[minimumIndex])
                return false;

            var beforeMinimum = minimumIndex - 1;
            while (beforeMinimum >= start && lower[beforeMinimum] == lower[minimumIndex])
                beforeMinimum--;
            if (beforeMinimum < start || lower[beforeMinimum] <= lower[minimumIndex])
                return false;

            troughIndex = minimumIndex;
            return true;
        }
    }
}
