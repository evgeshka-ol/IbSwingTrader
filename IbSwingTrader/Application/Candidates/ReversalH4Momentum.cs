using System.Globalization;

namespace IbSwingTrader.Application.Candidates
{
    public static class ReversalH4Momentum
    {
        public static ReversalRecoveryMomentum Evaluate(IReadOnlyList<Candle> completed,
            ExecutionPriceSnapshot? quote, DateTime decisionAt, DateTime expectedLastClosedStart)
        {
            ReversalRecoveryMomentum Unavailable(string reason) => new(false,
                $"RecoveryMomentum(Source=Unavailable, Reason={reason})");
            if (quote == null || quote.Price <= 0m)
                return Unavailable("no-fresh-price");
            var currentStart = decisionAt.AddTicks(-(decisionAt.Ticks % TimeSpan.FromHours(4).Ticks));
            if (currentStart.Hour is not (4 or 8 or 12 or 16))
                return Unavailable("outside-native-H4-session");
            if (quote.PriceTime <= currentStart || quote.PriceTime > decisionAt.AddSeconds(2) ||
                quote.ObservedAt > decisionAt.AddSeconds(2) || quote.ObservedAt < quote.PriceTime.AddSeconds(-2) ||
                decisionAt - quote.PriceTime > TimeSpan.FromMinutes(10) ||
                decisionAt - quote.ObservedAt > TimeSpan.FromMinutes(10))
                return Unavailable("stale-or-misaligned-price");
            if (completed.Count < 2 || completed[^1].Time != expectedLastClosedStart ||
                completed.Any(x => x.Time.AddHours(4) > currentStart) ||
                completed.Zip(completed.Skip(1), (a, b) => b.Time <= a.Time).Any(x => x))
                return Unavailable("missing-or-unaligned-closed-H4");
            var macd = FeatureEngine.CalculateMacdWithCurrentPrice(
                completed.Select(x => x.Close).ToList(), quote.Price);
            if (!macd.HasValue)
                return Unavailable("invalid-H4-closes");
            var m = macd.Value;
            var improving = m.Histogram > m.PreviousHistogram;
            string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
            return new(improving,
                $"RecoveryMomentum(Source=FreshPriceProvisionalH4, Improving={improving}, " +
                $"PartialStart={currentStart:O}, DecisionAt={decisionAt:O}, " +
                $"LastClosedH4={completed[^1].Time:O}, PriceSource={quote.Source}, " +
                $"PriceTime={quote.PriceTime:O}, ObservedAt={quote.ObservedAt:O}, " +
                $"Price={N(quote.Price)}, PreviousHistogram={N(m.PreviousHistogram)}, " +
                $"MacdLine={N(m.Line)}, MacdSignal={N(m.Signal)}, Histogram={N(m.Histogram)})");
        }
    }
}
