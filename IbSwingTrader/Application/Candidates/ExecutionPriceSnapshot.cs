namespace IbSwingTrader.Application.Candidates
{
    public sealed record ExecutionPriceSnapshot(
        decimal Price, DateTime PriceTime, DateTime ObservedAt, string Source,
        Candle? Previous, Candle? Current)
    {
        public decimal? ProjectedEntryPrice => Previous != null && Current != null &&
            Previous.Close > Previous.Open
                ? decimal.Round(Current.Open + Previous.Close - Previous.Open, 2,
                    MidpointRounding.AwayFromZero)
                : null;

        public static ExecutionPriceSnapshot? FromM5(
            IEnumerable<Candle> candles, DateTime requestedAt, DateTime observedAt)
        {
            var bucket = requestedAt.AddTicks(-(requestedAt.Ticks % TimeSpan.FromMinutes(5).Ticks));
            var bars = candles.Where(x => x.Time <= requestedAt && x.Open > 0m && x.Close > 0m)
                .OrderBy(x => x.Time).ToList();
            var current = bars.LastOrDefault(x => x.Time == bucket);
            var previous = bars.LastOrDefault(x => x.Time == bucket.AddMinutes(-5));
            // No old-session fallback masquerading as a live price.
            var latest = current ?? previous;
            if (latest == null)
                return null;

            return new ExecutionPriceSnapshot(latest.Close,
                current != null ? requestedAt : latest.Time.AddMinutes(5), observedAt,
                current != null ? "M5PartialClose" : "M5CompletedClose", previous, current);
        }

        public void ApplyTo(TradePlanInfo plan)
        {
            plan.ReferencePriceTime = PriceTime;
            plan.ReferencePriceBarTime = Current?.Time ?? Previous?.Time;
            plan.ReferencePriceObservedAt = ObservedAt;
            plan.ReferencePriceSource = Source;
            plan.EntryPriceSource = ProjectedEntryPrice.HasValue
                ? "BellUpM5BodyContinuation"
                : "BellUpFreshM5PriceFallback";
            plan.M5PreviousBarTime = Previous?.Time;
            plan.M5PreviousOpen = Previous?.Open;
            plan.M5PreviousClose = Previous?.Close;
            plan.M5CurrentBarTime = Current?.Time;
            plan.M5CurrentOpen = Current?.Open;
            plan.M5CurrentClose = Current?.Close;
            plan.M5ProjectedEntryPrice = ProjectedEntryPrice;
        }
    }
}
