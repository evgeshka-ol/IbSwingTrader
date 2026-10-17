using IbSwingTrader.Application.Candidates;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
// VSTM-like saved closed-Daily seed, followed by weak yesterday momentum.
List<decimal> price = [8.1m, 7.9m, 7.7m, 7.75m, 8.27m, 8.11m, 8.38m, 8.25m, 7.45m, 7.71m];
List<decimal> mid = [8.1m, 8m, 7.9m, 7.7605m, 7.777m, 7.8015m, 7.8545m, 7.8915m, 7.897m, 7.9165m];
List<decimal> hist = [-0.05m, -0.06m, -0.06m, -0.050338m, -0.022981m, -0.016864m,
    0.003100m, 0.005161m, -0.046876m, -0.062339m];
var upper = mid.Select(x => x + 1m).ToList();
var recoveringH4 = new ReversalRecoveryMomentum(true, "RecoveryMomentum(Source=FreshPriceProvisionalH4, Improving=True)");
var weakH4 = new ReversalRecoveryMomentum(false, "RecoveryMomentum(Source=FreshPriceProvisionalH4, Improving=False)");
Check(ReversalHookPhaseClassifier.Analyze(price, mid, hist, 8.11m, upper).Name == "Stalled",
    "Without fresh H4 evidence the existing Daily momentum veto remains");
var resumed = ReversalHookPhaseClassifier.Analyze(price, mid, hist, 8.11m, upper, recoveringH4);
Check(resumed.Name == "MidCrossed" && resumed.BarsSinceBend == 5,
    "Fresh improving H4 can revive an existing Daily seed at mid reclaim");
Check(resumed.Diagnostics.Contains("FreshPriceProvisionalH4"), "Retain evidence provenance");
Check(ReversalHookPhaseClassifier.Analyze(price, mid, hist, 8.11m, upper, weakH4).Name == "Stalled",
    "Non-improving H4 cannot override Daily weakness");
Check(ReversalHookPhaseClassifier.Analyze(price, mid, hist, 7.70m, upper, recoveringH4).Name == "Stalled",
    "A price below the pre-bend close still invalidates recovery");
Check(ReversalHookPhaseClassifier.Analyze(price, mid, hist, 7.80m, upper, recoveringH4).Name == "Stalled",
    "H4 momentum alone cannot resume without a Daily mid reclaim");
Check(ReversalHookPhaseClassifier.Analyze(price, mid, hist, 9.50m, upper, recoveringH4).Name == "Completed",
    "Reaching upper remains Completed, not a revived playable phase");
Check(ReversalHookPhaseClassifier.Analyze(price, Enumerable.Repeat(8m, price.Count).ToList(),
    hist, 8.11m, upper, recoveringH4).BarsSinceBend == -1,
    "Provisional H4 cannot manufacture a historical Daily seed");

var decision = new DateTime(2026, 10, 15, 6, 35, 48);
var previousStart = new DateTime(2026, 10, 14, 16, 0, 0);
var closed = Enumerable.Range(0, 60).Select(i => new Candle
{
    Time = previousStart.AddHours(-4 * (59 - i)), Close = 10m - i * 0.02m
}).ToList();
var quote = new ExecutionPriceSnapshot(closed[^1].Close + 1m,
    decision.AddSeconds(-2), decision, "IBLiveAskObserved", null, null);
var evidence = ReversalH4Momentum.Evaluate(closed, quote, decision, previousStart);
Check(evidence.IsImproving, "A current live price supplies close-only provisional H4 momentum");
Check(evidence.Diagnostics.Contains("PartialStart=2026-10-15T04:00:00"), "Use native current H4 interval");
Check(!ReversalH4Momentum.Evaluate(closed, null, decision, previousStart).IsImproving,
    "HistoricalSnapshot is not fresh evidence");
Check(!ReversalH4Momentum.Evaluate(closed, quote with { PriceTime = decision.AddMinutes(-11) },
    decision, previousStart).IsImproving, "Reject stale price");
Check(!ReversalH4Momentum.Evaluate(closed, quote with { PriceTime = decision.AddMinutes(5) },
    decision, previousStart).IsImproving, "Reject future price");
Check(!ReversalH4Momentum.Evaluate(closed, quote, decision, previousStart.AddHours(4)).IsImproving,
    "Require the expected previous closed native H4");
Check(!ReversalH4Momentum.Evaluate([.. closed, new Candle { Time = decision.Date.AddHours(4), Close = 15m }],
    quote, decision, previousStart).IsImproving, "Never use a final forming H4 from cache");
Check(!ReversalH4Momentum.Evaluate(closed, quote with { Price = 0m }, decision, previousStart).IsImproving,
    "Invalid price cannot authorize momentum support");
Check(!ReversalH4Momentum.Evaluate(closed, quote with { PriceTime = decision.Date.AddHours(4) },
    decision, previousStart).IsImproving, "A prior interval boundary price cannot stand in for current progress");

var before = closed.Select(x => (x.Time, x.Close)).ToList();
var point = FeatureEngine.CalculateMacdWithCurrentPrice(closed.Select(x => x.Close).ToList(), quote.Price)!.Value;
var native = FeatureEngine.CalculateNativeReversalHistory([.. closed,
    new Candle { Time = decision.Date.AddHours(4), Close = quote.Price }]);
Check(point.Histogram == native[^1].MACDHistogram && point.PreviousHistogram == native[^2].MACDHistogram,
    "Close-only provisional MACD agrees exactly with the existing MACD engine");
Check(before.SequenceEqual(closed.Select(x => (x.Time, x.Close))), "Do not mutate cache candles");
Console.WriteLine($"Passed {checks} reversal recovery checks.");
