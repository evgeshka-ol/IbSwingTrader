using System.Globalization;

namespace IbSwingTrader.Application.Candidates
{
    // Research diagnostics only: never mutate closed candles or authorize a trade.
    public static class ReversalSupplementalDiagnostics
    {
        private sealed record Series(List<decimal> Close, List<decimal> Mid,
            List<decimal> Upper, List<decimal> Histogram);

        private static Series Build(List<Candle> candles, Func<List<Candle>, int, FeatureSet> calculate)
        {
            var result = new Series([], [], [], []);
            for (var i = Math.Max(0, candles.Count - 10); i < candles.Count; i++)
            {
                var feature = calculate(candles, i + 1);
                result.Close.Add(candles[i].Close);
                result.Mid.Add(feature.H4BollingerMidBand);
                result.Upper.Add(feature.H4BollingerUpperBand);
                result.Histogram.Add(feature.MACDHistogram);
            }
            return result;
        }

        private static bool Gradual(Series series)
        {
            if (series.Mid.Count < 5 || series.Mid.TakeLast(5).Any(x => x <= 0m))
                return false;
            var m = series.Mid;
            var deltas = new[] { m[^4] - m[^5], m[^3] - m[^4], m[^2] - m[^3], m[^1] - m[^2] };
            return deltas.All(x => x < 0m) && deltas[^1] > deltas[0] &&
                deltas.Zip(deltas.Skip(1), (a, b) => b > a).Count(x => x) >= 2 &&
                series.Close[^1] > series.Close[^3] &&
                series.Histogram[^1] > series.Histogram[^2] && series.Histogram[^2] >= series.Histogram[^3] &&
                series.Close.TakeLast(4).Min() < m[^4];
        }

        public static string Describe(List<Candle> completed, IEnumerable<Candle> bridge,
            DateTime decisionAt, Func<List<Candle>, int, FeatureSet> calculate)
        {
            var currentStart = decisionAt.AddTicks(-(decisionAt.Ticks % TimeSpan.FromHours(4).Ticks));
            var prefix = $"ReversalSupplemental(DiagnosticOnly=True, DecisionAt={decisionAt:O}";
            if (completed.Count < 20)
                return prefix + ", Status=InsufficientH4History)";
            var closed = Build(completed, calculate);
            prefix += $", ClosedH4Gradual={Gradual(closed)}, PartialStart={currentStart:O}";
            // All inputs were returned by this run's bounded bridge/snapshot requests.
            var bars = bridge.Where(x => x.Time >= currentStart && x.Time.AddMinutes(5) <= decisionAt)
                .GroupBy(x => x.Time).Select(x => x.Last()).OrderBy(x => x.Time).ToList();
            var observed = bars.Where(x => x.Volume > 0m && x.Open > 0m && x.Close > 0m).ToList();
            prefix += $", M5Bars={bars.Count}, ObservedM5Bars={observed.Count}";
            if (observed.Count == 0)
                return prefix + ", PartialStatus=NoObservedM5)";
            var gaps = bars.Zip(bars.Skip(1), (a, b) => b.Time - a.Time > TimeSpan.FromMinutes(5)).Count(x => x);
            var coversStart = bars[0].Time == currentStart;
            var age = decisionAt - observed[^1].Time.AddMinutes(5);
            prefix += $", CoversStart={coversStart}, Gaps={gaps}, LastObservedM5={observed[^1].Time:O}";
            if (!coversStart || gaps > 0 || age > TimeSpan.FromMinutes(10) ||
                bars.Any(x => x.Open <= 0m || x.Close <= 0m || x.High < x.Low))
                return prefix + ", PartialStatus=IncompleteOrStale)";
            var partial = new Candle
            {
                Timeframe = Timeframe.H4, Time = currentStart,
                Open = bars[0].Open, High = bars.Max(x => x.High), Low = bars.Min(x => x.Low),
                Close = observed[^1].Close, Volume = observed.Sum(x => x.Volume)
            };
            var withPartial = new List<Candle>(completed) { partial };
            var feature = calculate(withPartial, withPartial.Count);
            // Previous indicator points equal the closed series; calculate only the new point.
            var provisional = new Series([.. closed.Close, partial.Close],
                [.. closed.Mid, feature.H4BollingerMidBand], [.. closed.Upper, feature.H4BollingerUpperBand],
                [.. closed.Histogram, feature.MACDHistogram]);
            var phase = ReversalHookPhaseClassifier.Analyze(provisional.Close, provisional.Mid,
                provisional.Histogram, partial.Close, provisional.Upper);
            string N(decimal number) => number.ToString(CultureInfo.InvariantCulture);
            return prefix + $", PartialStatus=Fresh, Source=ReturnedCompletedM5, " +
                $"PartialOpen={N(partial.Open)}, PartialHigh={N(partial.High)}, PartialLow={N(partial.Low)}, " +
                $"PartialClose={N(partial.Close)}, PartialVolume={N(partial.Volume)}, " +
                $"PartialBbUpper={N(feature.H4BollingerUpperBand)}, PartialBbMid={N(feature.H4BollingerMidBand)}, " +
                $"PartialBbLower={N(feature.H4BollingerLowerBand)}, PartialMacdLine={N(feature.MACDLine)}, " +
                $"PartialMacdSignal={N(feature.MACDSignal)}, PartialMacdHistogram={N(feature.MACDHistogram)}, " +
                $"PartialRsi={N(feature.RSI14)}, PartialGradual={Gradual(provisional)}, " +
                $"PartialPhase={phase.Name}, PartialBarsSinceBend={phase.BarsSinceBend})";
        }
    }
}
