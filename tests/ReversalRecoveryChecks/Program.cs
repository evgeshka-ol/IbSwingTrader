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
// RCUS-like early H4 recovery after three declining closed after-hours prices.
List<decimal> earlyPrice = [22m, 21.9m, 21.8m, 21.6m, 22.21m, 22.15m, 22.14m];
List<decimal> earlyMid = [23.6m, 23.4m, 23.2m, 23m, 22.9m, 22.8m, 22.72m];
List<decimal> earlyHist = [-.1m, -.12m, -.13m, -.14m, -.04m, -.02m, -.001m];
var earlyUpper = earlyMid.Select(x => x + 1m).ToList();
Check(ReversalHookPhaseClassifier.Analyze(earlyPrice, earlyMid, earlyHist, 22.26m,
    earlyUpper).Name == "Stalled", "Closed declining prices alone do not prove resumed recovery");
var early = ReversalHookPhaseClassifier.Analyze(earlyPrice, earlyMid, earlyHist, 22.26m,
    earlyUpper, recoveringH4);
Check(early.Name == "Active" && early.BarsSinceBend == 2 &&
    early.Diagnostics.Contains("FreshEarlyRecovery=True"),
    "Fresh improving H4 above recent closes can resume the recognized episode below mid");
Check(ReversalHookPhaseClassifier.Analyze(earlyPrice, earlyMid, earlyHist, 22.26m,
    earlyUpper, weakH4).Name == "Stalled", "Early recovery requires improving fresh H4 momentum");
Check(ReversalHookPhaseClassifier.Analyze(earlyPrice, earlyMid, earlyHist, 22.21m,
    earlyUpper, recoveringH4).Name == "Stalled", "A quote merely touching the recent high is insufficient");
Check(ReversalHookPhaseClassifier.Analyze(earlyPrice, earlyMid, earlyHist, 21.59m,
    earlyUpper, recoveringH4).Name == "Stalled", "Fresh momentum cannot override episode price invalidation");
var fadedHist = earlyHist.ToList();
fadedHist[^1] = -.05m;
Check(ReversalHookPhaseClassifier.Analyze(earlyPrice, earlyMid, fadedHist, 22.26m,
    earlyUpper, recoveringH4).Name == "Stalled", "Early recovery retains closed episode momentum support");
var earlyHigh = earlyPrice.ToList();
earlyHigh[4] = earlyUpper[4];
Check(ReversalHookPhaseClassifier.Analyze(earlyPrice, earlyMid, earlyHist, 22.26m,
    earlyUpper, recoveringH4, earlyHigh).Name == "Completed", "Early recovery cannot revive an upper-completed impulse");
// A weak Daily MACD need not veto a recovery already established on native H4.
List<decimal> rcusMid = [22.85072698m, 22.78399108m, 22.72265859m];
List<decimal> rcusLower = [21.1m, 21.13064223m, 21.22809483m];
List<decimal> rcusHistogram = [-.0196862955m, -.00130027987m];
bool H4Support(string phase = "Active", string regime = "Neutral", bool recognized = true,
    decimal priceNow = 22.26m, ReversalRecoveryMomentum? fresh = null,
    IReadOnlyList<decimal>? mids = null, IReadOnlyList<decimal>? lowers = null,
    IReadOnlyList<decimal>? histograms = null, IReadOnlyList<decimal>? dailyMids = null)
    => ReversalH4DailySupport.Allows(recognized, phase, regime, priceNow,
        dailyMids ?? [24.15007708m], mids ?? rcusMid, lowers ?? rcusLower,
        histograms ?? rcusHistogram, fresh ?? recoveringH4, out _);
Check(H4Support(), "RCUS-like active H4 can support a below-Daily-mid recovery with negative histogram");
Check(ReversalH4DailySupport.Allows(true, "MidCrossed", "Neutral", 36.59m,
    [37.99503527m], [34.549m, 34.321m, 34.144m],
    [29.34237913m, 29.37939521m, 29.64370227m],
    [.01200394m, .10450058m], recoveringH4, out var h4SupportReason),
    "SMCI-like evening H4 recovery does not wait for Daily MACD positivity");
Check(h4SupportReason.Contains("H4-led recovery") && h4SupportReason.Contains("FreshPriceProvisionalH4"),
    "Alternative support retains evidence and a distinct auditable admission reason");
Check(!H4Support(regime: "Collapse"), "Daily Collapse is still a hard veto");
Check(!H4Support(recognized: false), "Fresh momentum cannot manufacture hook recognition");
foreach (var inactivePhase in new[] { "None", "Preparing", "Stalled", "Completed", "TargetReached" })
    Check(!H4Support(phase: inactivePhase), $"{inactivePhase} cannot use H4-led admission");
Check(!H4Support(fresh: weakH4), "A weakening fresh H4 cannot bypass Daily support");
Check(!H4Support(fresh: new(false, "RecoveryMomentum(Source=Unavailable, Reason=stale-or-misaligned-price)")),
    "Unavailable or stale quote momentum cannot bypass Daily support");
Check(!H4Support(histograms: [-.001m, -.002m]), "Closed H4 deterioration blocks alternative support");
Check(!H4Support(histograms: [-.001m, -.001m]), "Flat closed momentum does not prove recovery");
Check(!H4Support(lowers: [21.2m, 21.1m]), "A falling lower band is not an upward H4 hook");
Check(!H4Support(lowers: [21.1m, 21.1m]), "A flat lower band is insufficient");
Check(!H4Support(mids: [23m, 22.9m, 22.7m]), "Renewed downward mid acceleration blocks support");
Check(H4Support(mids: [22.9m, 22.8m, 22.8m]), "A flattened mid can support a rising lower-band hook");
Check(H4Support(mids: [22.730m, 22.725m, 22.722m], lowers: [21.101m, 21.102m],
    histograms: [-.0012m, -.0011m]), "Sub-cent band and histogram improvements retain full precision");
Check(!H4Support(priceNow: 24m), "Insufficient remaining room to Daily mid cannot use alternative support");
Check(H4Support(priceNow: 100m, dailyMids: [102m]), "The existing two-percent room boundary remains inclusive");
Check(!H4Support(priceNow: 24.2m), "Above-Daily-mid setups use the ordinary support path");
Check(!H4Support(mids: []), "Missing native H4 geometry cannot be replaced with fresh price alone");
Check(!H4Support(dailyMids: [0m]), "Invalid Daily reference cannot authorize support");
var nnDaily = System.Text.Json.JsonSerializer.Deserialize<List<Candle>>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nn-october23-daily.json")))!;
var nnAt = new DateTime(2026, 10, 23, 17, 13, 14);
List<FeatureSet> nnH4 = [new() { MACDHistogram = .08742711606870982m },
    new() { MACDHistogram = .12952010230659362m }];
var nnFresh = new ReversalRecoveryMomentum(true,
    "RecoveryMomentum(Source=FreshPriceProvisionalH4, PriceSource=M5PartialClose, PriceTime=2026-10-23T17:13:12, Improving=True)");
bool WeeklySupport(string origin = "DeepDeclineRecovery", bool recognized = true,
    string phase = "Active", string regime = "Neutral", List<Candle>? daily = null,
    List<FeatureSet>? h4 = null, decimal quote = 10.1m,
    ReversalRecoveryMomentum? fresh = null, DateTime? at = null)
    => ReversalDailyWeeklySupport.Allows(origin, recognized, phase, regime, daily ?? nnDaily,
        h4 ?? nnH4, quote, fresh ?? nnFresh, at ?? nnAt, out _);
Check(WeeklySupport(), "NN frozen scan-time D1 prefix supports a weekly recovery exception");
Check(ReversalDailyWeeklySupport.Allows("DeepDeclineRecovery", true, "Active", "Neutral",
    nnDaily, nnH4, 10.1m, nnFresh, nnAt, out var nnEvidence) &&
    nnEvidence.Contains("WeekStart=2026-10-19") && nnEvidence.Contains("PreviousObservation=2026-10-22") &&
    nnEvidence.Contains("LatestObservation=2026-10-23") && nnEvidence.Contains("Source=CompletedNativeD1") &&
    nnEvidence.Contains(nnFresh.Diagnostics), "Weekly evidence records same-week observations and fresh H4 provenance");
Check(!WeeklySupport(regime: "Collapse"), "W1 Collapse still blocks the exception");
Check(!WeeklySupport(origin: "PostBellUpRecovery"), "Post-BellUp does not borrow deep-decline support");
Check(!WeeklySupport(origin: "Unclassified"), "Unknown origins cannot bypass W1 Down");
Check(!WeeklySupport(recognized: false), "Weekly recovery never recognizes a new hook");
foreach (var phase in new[] { "Completed", "Stalled", "Invalidated" })
    Check(!WeeklySupport(phase: phase), $"{phase} cannot borrow weekly recovery");
Check(!WeeklySupport(fresh: new(false, "Unavailable")), "Stale or weak fresh H4 cannot authorize weekly recovery");
Check(!WeeklySupport(h4: []), "Missing closed H4 evidence cannot authorize weekly recovery");
Check(!WeeklySupport(h4: [new() { MACDHistogram = .2m }, new() { MACDHistogram = .1m }]),
    "Closed H4 deterioration blocks the exception");
Check(!WeeklySupport(quote: 11.44m), "Room to Daily mid is required");
Check(!WeeklySupport(at: nnAt.Date.AddHours(15)), "The final Daily close cannot be used before session close");
Check(!WeeklySupport(at: nnAt.AddDays(3)), "A previous week's observation is not current-week momentum");
Check(!WeeklySupport(daily: nnDaily.Take(100).ToList()), "Short or old history cannot prove weekly recovery");
var mondayPrefix = nnDaily.Where(x => x.Time.Date <= new DateTime(2026, 10, 19)).ToList();
Check(!WeeklySupport(daily: mondayPrefix, at: new DateTime(2026, 10, 19, 17, 0, 0)),
    "Monday cannot compare two completed closes from the same week");
var weakerDaily = nnDaily.Select(x => new Candle { Time = x.Time, Close = x.Close }).ToList();
weakerDaily[^1].Close = weakerDaily[^2].Close - .01m;
Check(!WeeklySupport(daily: weakerDaily), "A renewed decline cannot masquerade as weekly recovery");
Check(!ReversalDailyWeeklySupport.Allows("DeepDeclineRecovery", true, "Active", "Neutral",
    weakerDaily, nnH4, 10.1m, nnFresh, nnAt, out var weakWeeklyReason) &&
    weakWeeklyReason.Contains("current-week-MACD-not-improving"),
    "Daily momentum improvement alone cannot replace same-week MACD improvement");
var outOfOrderDaily = nnDaily.ToList();
outOfOrderDaily.Reverse();
Check(!WeeklySupport(daily: outOfOrderDaily), "Nonchronological history is rejected");
Check(nnDaily[^1].Close == 10.08m && nnDaily[^1].Time.Date == nnAt.Date,
    "Support analysis leaves the frozen prefix unchanged and contains no October26 peak");
Console.WriteLine($"Passed {checks} reversal recovery checks.");
