namespace IbSwingTrader.Application.Dataset
{
    public static class EvaluationAmplitude
    {
        public static decimal Calculate(
            decimal scanPrice, decimal entryPrice, DateTime? entryTime,
            decimal? maxPrice, decimal? maxPct, DateTime? maxTime,
            decimal? minPrice, decimal? minPct, DateTime? minTime,
            string? candidateGroup = null)
        {
            var high = ResolvePrice(maxPrice, maxPct, scanPrice);
            if (!high.HasValue)
                return 0m;

            // Other has no executable plan: measure upside from the published
            // reference, without inferring a later entry or an extrema-order sign.
            if (string.Equals(candidateGroup, "Other", StringComparison.OrdinalIgnoreCase))
                return Percent(scanPrice, high.Value);

            // Participation is established by EntryTime, not by nonzero plan levels.
            if (entryTime.HasValue)
                return Percent(entryPrice, high.Value);

            // Without a fill, only a strictly earlier minimum establishes min-first.
            // Equal/missing timestamps cannot establish the intrabar path: use ScanPrice.
            var low = ResolvePrice(minPrice, minPct, scanPrice);
            var minFirst = minTime.HasValue && maxTime.HasValue && minTime.Value < maxTime.Value &&
                low.HasValue && (scanPrice <= 0m || low.Value < scanPrice);
            var value = Percent(minFirst ? low ?? scanPrice : scanPrice, high.Value);
            // Retain the existing negative marker for a known max-first diagnostic.
            return minTime.HasValue && maxTime.HasValue && maxTime.Value < minTime.Value
                ? -Math.Abs(value)
                : value;
        }

        private static decimal? ResolvePrice(decimal? price, decimal? pct, decimal scanPrice)
            => price is > 0m ? price
                : scanPrice > 0m && pct.HasValue && pct.Value > -100m
                    ? scanPrice * (1m + pct.Value / 100m)
                    : null;

        private static decimal Percent(decimal reference, decimal price)
            => reference > 0m ? Math.Round((price / reference - 1m) * 100m, 2,
                MidpointRounding.AwayFromZero) : 0m;
    }
}
