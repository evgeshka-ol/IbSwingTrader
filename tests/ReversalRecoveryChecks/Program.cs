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

// A completed upper-band excursion belongs to the episode even after price and bands move away.
var high = price.ToList();
high[4] = upper[4];
var finished = ReversalHookPhaseClassifier.Analyze(price, mid, hist, 8.11m, upper, recoveringH4, high);
Check(finished.Name == "Completed", "A wick touching upper at the seed consumes the impulse");
Check(finished.Diagnostics.Contains("UpperTouchSource=CompletedHigh") &&
    finished.Diagnostics.Contains("UpperTouchBarsAgo=5"), "Record touch evidence rather than just a Completed label");
var expandedUpper = upper.ToList();
expandedUpper[^1] = 20m;
Check(ReversalHookPhaseClassifier.Analyze(price, mid, hist, 8.11m, expandedUpper, recoveringH4, high).Name == "Completed",
    "Later band expansion and a fresh recovering quote cannot revive the completed episode");
var beforeSeedHigh = price.ToList();
beforeSeedHigh[3] = upper[3] + 1m;
Check(ReversalHookPhaseClassifier.Analyze(price, mid, hist, 8.11m, upper, recoveringH4, beforeSeedHigh).Name == "MidCrossed",
    "A touch before this signal does not consume it");
var closeTouchUpper = upper.ToList();
closeTouchUpper[4] = 8.20m;
Check(ReversalHookPhaseClassifier.Analyze(price, mid, hist, 8.11m, closeTouchUpper, recoveringH4).Name == "Completed",
    "Saved close alone proves an earlier touch when highs are unavailable");
var invalidUpper = upper.ToList();
invalidUpper[4] = mid[4];
Check(ReversalHookPhaseClassifier.Analyze(price, mid, hist, 8.11m, invalidUpper, recoveringH4, high).Name == "MidCrossed",
    "Invalid zero-width band cannot establish completion");
Check(ReversalImpulseCompletion.FindTouch(price, mid, upper, 0, high) == null,
    "A new signal boundary excludes the preceding impulse's touch");

List<decimal> newPrice = [.. price, 7.6m, 7.4m, 7.2m, 7.5m];
List<decimal> newMid = [.. mid, 7.85m, 7.75m, 7.65m, 7.60m];
List<decimal> newHist = [.. hist, -0.07m, -0.08m, -0.09m, -0.06m];
List<decimal> newHigh = [.. high, 7.6m, 7.4m, 7.2m, 7.5m];
var newUpper = newMid.Select(x => x + 1m).ToList();
var independent = ReversalHookPhaseClassifier.Analyze(newPrice, newMid, newHist, 7.5m, newUpper, null, newHigh);
Check(independent.BarsSinceBend == 0 && independent.Name == "Active",
    "A newly recognized independent bend is not permanently blacklisted by an earlier completion");

var friday = new DateTime(2026, 10, 16);
var touch = new ReversalUpperTouch(1, 13.80m, 13.51m, "CompletedHigh");
Check(ReversalImpulseCompletion.FindTouch([13.77m], [13.22m], [13.87m], 0, [13.80m]) == null,
    "CRGY Daily near-touch is not rounded into a true upper-band touch");
Check(ReversalImpulseCompletion.FromClosedBar("H4", friday.AddHours(8), friday.AddHours(12),
    touch, friday.AddHours(15)) == null, "A forming H4 cannot establish historical completion");
var crgyH4 = ReversalImpulseCompletion.FromClosedBar("H4", friday.AddHours(8), friday.AddHours(12),
    touch, friday.AddHours(16))!;
Check(crgyH4.ConfirmedAt == friday.AddHours(16) && crgyH4.High == 13.80m && crgyH4.Upper == 13.51m,
    "CRGY-like closed H4 retains native completion time and exact band evidence");
Check(ReversalImpulseCompletion.FromClosedBar("H4", friday.AddHours(8), friday.AddHours(12),
    touch with { Source = "LivePrice" }, friday.AddHours(16)) == null,
    "A live or provisional touch is not a completed historical H4 event");
var dailyEpisode = new ReversalFrameEpisode(true, "D1", "MidCrossed", "saved D1 episode", friday, null);
var h4Episode = new ReversalFrameEpisode(true, "H4", "Completed", "saved H4 episode", friday.AddHours(8), crgyH4);
var consumedDaily = ReversalImpulseCompletion.ApplyH4Completion(dailyEpisode, h4Episode);
Check(consumedDaily.Phase == "Completed" && consumedDaily.Recognized,
    "Same-episode H4 completion blocks D1 readmission while retaining recognition");
Check(consumedDaily.Reason.Contains("H4-impulse-already-completed-in-D1-episode") &&
    consumedDaily.Reason.Contains("ConfirmedAt=2026-10-16T16:00:00"), "Admission reason carries auditable H4 evidence");
Check(ReversalImpulseCompletion.ApplyH4Completion(dailyEpisode with { SignalBar = friday.AddDays(3) }, h4Episode).Phase == "MidCrossed",
    "An older completed H4 does not veto a newer Daily episode");
Check(ReversalImpulseCompletion.ApplyH4Completion(dailyEpisode, h4Episode with
    { Completion = crgyH4 with { SignalBar = friday.AddDays(-1) } }).Phase == "MidCrossed",
    "A pre-existing H4 episode is not silently treated as contained in the new Daily one");
Check(ReversalImpulseCompletion.ApplyH4Completion(dailyEpisode, h4Episode with
    { Phase = "Active", Completion = null, SignalBar = friday.AddDays(3).AddHours(4) }).Phase == "MidCrossed",
    "A new independent H4 signal remains eligible for normal admission");
Check(ReversalImpulseCompletion.ApplyH4Completion(dailyEpisode, h4Episode with { Recognized = false }).Phase == "MidCrossed",
    "Unrecognized H4 evidence cannot veto a Daily episode");
Console.WriteLine($"Passed {checks} reversal recovery checks.");
