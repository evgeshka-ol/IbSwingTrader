namespace IbSwingTrader.Application.Candidates
{
    public sealed record ReversalOrigin(string Kind, int BelowMidBars, decimal MaxDepthPct,
        DateTime? PriorBellUpBar, string Diagnostics);

    // Describes a recognized hook's origin, never recognizes or admits a trade.
    public static class ReversalOriginClassifier
    {
        public const int ContextBars = 10;
        private const int PriorBellUpSearchBars = 30;

        public static ReversalOrigin Analyze(IReadOnlyList<Candle> completedDaily)
        {
            ReversalOrigin Unknown(string reason) => new("Unclassified", 0, 0m, null,
                $"ReversalOrigin(Kind=Unclassified, Reason={reason}, Model={FeatureEngine.BollingerModel})");
            if (completedDaily.Count < 20 + ContextBars - 1 || completedDaily.Any(x => x.Close <= 0m) ||
                completedDaily.Zip(completedDaily.Skip(1), (a, b) => b.Time <= a.Time).Any(x => x))
                return Unknown("insufficient-or-invalid-completed-D1");
            var bars = completedDaily.ToList();
            var features = FeatureEngine.CalculateNativeReversalHistory(bars);
            var contextStart = bars.Count - ContextBars;
            // The latest below-mid run can be ongoing or recently reclaimed.
            var end = bars.Count - 1;
            while (end >= contextStart && bars[end].Close >= features[end].DailyBollingerMidBand)
                end--;
            if (end < contextStart)
                return Unknown("no-below-mid-episode-in-context");
            var start = end;
            while (start > 19 && bars[start - 1].Close < features[start - 1].DailyBollingerMidBand)
                start--;
            var length = end - start + 1;
            var depth = Enumerable.Range(start, length)
                .Max(i => (1m - bars[i].Close / features[i].DailyBollingerMidBand) * 100m);
            var allContextBelow = Enumerable.Range(contextStart, ContextBars)
                .All(i => bars[i].Close < features[i].DailyBollingerMidBand);
            DateTime? priorBellUp = null;
            var analyzer = new BollingerFigureAnalyzer();
            // Keep the long indicator prefix; inspect preceding closed pattern windows.
            // This avoids calling every short pullback "post-BellUp" from duration alone.
            for (var index = start - 1; index >= Math.Max(24, start - PriorBellUpSearchBars); index--)
            {
                var window = features.Skip(Math.Max(19, index - 29)).Take(index - Math.Max(19, index - 29) + 1).ToList();
                var upper = window.Select(x => x.DailyBollingerUpperBand).ToList();
                var mid = window.Select(x => x.DailyBollingerMidBand).ToList();
                var lower = window.Select(x => x.DailyBollingerLowerBand).ToList();
                var state = analyzer.Analyze(new BollingerFeatureSeries
                    { UpperSeries = upper, MidSeries = mid, LowerSeries = lower });
                if (BellPatternClassifier.ClassifyBellPatternKindForTimeframe(upper, mid, lower,
                        state.Direction, BellPatternTimeframe.Daily) == BellPatternKind.BellUp)
                {
                    priorBellUp = bars[index].Time;
                    break;
                }
            }
            // Ten bars are an observation window, not a universal minimum holding time.
            // A deep run filling that window is a different origin from a recent cross.
            var kind = start <= contextStart && depth >= 5m ? "DeepDeclineRecovery"
                : start >= contextStart && priorBellUp.HasValue ? "PostBellUpRecovery"
                : "Unclassified";
            var beforeStart = Math.Max(19, start - 4);
            var beforeEnd = Math.Max(19, start - 1);
            decimal Pct(decimal a, decimal b) => a > 0m ? (b / a - 1m) * 100m : 0m;
            var priorSlope = Pct(features[beforeStart].DailyBollingerMidBand, features[beforeEnd].DailyBollingerMidBand);
            var sinceSlope = Pct(features[beforeEnd].DailyBollingerMidBand, features[^1].DailyBollingerMidBand);
            var diagnostics = FormattableString.Invariant(
                $"ReversalOrigin(Kind={kind}, ContextBars={ContextBars}, BelowMidBars={length}, MaxDepthPct={depth:0.####}, RunStart={bars[start].Time:yyyy-MM-dd}, RunEnd={bars[end].Time:yyyy-MM-dd}, Ongoing={end == bars.Count - 1}, AllContextBelow={allContextBelow}, LeftCensored={start == 19}, PriorBellUpBar={priorBellUp:yyyy-MM-dd}, PriorSearchBars={PriorBellUpSearchBars}, MidSlopeBeforeCrossPct={priorSlope:0.####}, MidSlopeSinceCrossPct={sinceSlope:0.####}, Model={FeatureEngine.BollingerModel})");
            return new(kind, length, depth, priorBellUp, diagnostics);
        }
    }
}
