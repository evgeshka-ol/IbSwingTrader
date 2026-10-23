using System.Text.Json;
using IbSwingTrader.Application.Candidates;
using IbSwingTrader.Application.Evaluation;
using IbSwingTrader.Domain.Candidates;
using IbSwingTrader.Domain.Dataset;
using IbSwingTrader.Domain.Market;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
var fixture = JsonSerializer.Deserialize<Dictionary<string, List<Candle>>>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "october21-daily.json")))!;
foreach (var (ticker, bars) in fixture)
{
    var result = ReversalOriginClassifier.Analyze(bars);
    var expected = ticker is "GRAL" or "DDOG" or "OKTA" ? "PostBellUpRecovery" : "DeepDeclineRecovery";
    Check(result.Kind == expected, $"{ticker}: expected {expected}, got {result.Diagnostics}");
    Check(result.BelowMidBars > 0 && result.MaxDepthPct > 0m, $"{ticker}: retain measured run evidence");
    Check(result.Diagnostics.Contains("ContextBars=10") && result.Diagnostics.Contains("ema20-sma-seed20"),
        $"{ticker}: label indicator model and context window");
    if (expected == "PostBellUpRecovery")
        Check(result.PriorBellUpBar.HasValue, $"{ticker}: short duration alone cannot prove prior BellUp");
}

var start = new DateTime(2026, 1, 1);
List<Candle> History(params decimal[] prices) => prices.Select((p, i) => new Candle
    { Time = start.AddDays(i), Open = p, High = p, Low = p, Close = p }).ToList();
var flat = Enumerable.Repeat(100m, 80).ToArray();
Check(ReversalOriginClassifier.Analyze(History(flat)).Kind == "Unclassified", "Flat price is not a below-mid episode");
Check(ReversalOriginClassifier.Analyze(History([.. flat, 90m, 90m, 100m])).Kind == "Unclassified",
    "A short dip without a preceding BellUp is not automatically PostBellUpRecovery");
var deep = History([.. flat, .. Enumerable.Repeat(80m, 20)]);
Check(ReversalOriginClassifier.Analyze(deep).Kind == "DeepDeclineRecovery", "Prolonged meaningful below-mid context");
Check(ReversalOriginClassifier.Analyze([.. deep, new Candle
    { Time = deep[^1].Time.AddDays(1), Open = 110m, High = 110m, Low = 110m, Close = 110m }]).Kind == "DeepDeclineRecovery",
    "A reclaim does not erase the preceding prolonged decline origin");
Check(ReversalOriginClassifier.Analyze(History([.. flat, .. Enumerable.Repeat(99.9m, 20)])).Kind == "Unclassified",
    "A prolonged tiny deviation is insufficient for the deep-decline subtype");
Check(ReversalOriginClassifier.Analyze(deep.Take(20).ToList()).Kind == "Unclassified", "Insufficient history is explicit");
Check(ReversalOriginClassifier.Analyze([.. deep, deep[^1]]).Kind == "Unclassified", "Reject duplicate/nonchronological Daily bars");
var before = deep.Select(x => (x.Time, x.Close)).ToList();
ReversalOriginClassifier.Analyze(deep);
Check(before.SequenceEqual(deep.Select(x => (x.Time, x.Close))), "Do not mutate source candles");
var gral = ReversalOriginClassifier.Analyze(fixture["GRAL"]);
Check(gral.BelowMidBars == 3, "GRAL EMA uses three below-mid closes; Yahoo SMA's four are not forced into the model");
Check(gral.Diagnostics.Contains("MidSlopeSinceCrossPct=-"),
    "GRAL can retain post-BellUp origin despite a temporary negative EMA slope after the cross");
var verdicts = new CandidatePatternVerdictService(new BollingerFigureAnalyzer());
foreach (var kind in new[] { "PostBellUpRecovery", "DeepDeclineRecovery", "Unclassified" })
{
    var prefixed = ReversalHookPhaseClassifier.WithOriginPrefix("ReversalHook confirmed on H4", kind);
    Check(prefixed == $"{kind}; ReversalHook confirmed on H4", "Subtype precedes the existing verdict");
    Check(ReversalHookPhaseClassifier.WithOriginPrefix(prefixed, kind) == prefixed,
        "Repeated formatting does not duplicate the origin prefix");
    var saved = new CandidateEvaluationResult { Ticker = "PREFIX", PresetScanCode = "CODE", PatternVerdictReason = prefixed };
    var verdict = verdicts.Analyze(saved);
    Check(verdict.PatternVerdict == "Match" && verdict.DetectedPattern == "ReversalHook" &&
        verdict.PatternVerdictReason == prefixed, "Evaluator recognizes and preserves the prefixed saved verdict");
    var dataset = new EvaluationDatasetRow { Ticker = "PREFIX", PresetScanCode = "CODE", GroupLabel = "",
        PatternVerdictReason = prefixed };
    Check(verdicts.Analyze(dataset).PatternVerdictReason == prefixed,
        "Dataset verdict interpretation retains the prefix");
    saved.PatternVerdictReason = $"{kind}; ReversalHook unconfirmed on D1/H4";
    Check(verdicts.Analyze(saved).PatternVerdict == "Mismatch", "A prefix cannot turn unconfirmed into a match");
    Check(ReversalHookPhaseClassifier.TryGetSavedPattern(
        $"{kind}; ReversalHookActive detected on H4; DiagnosticOnly=True; saved", out var phase) &&
        phase == "ReversalHookActive", "Legacy diagnostic phases also accept the prefix");
}
Check(ReversalHookPhaseClassifier.WithOriginPrefix("BellUp confirmed on H4", "PostBellUpRecovery") ==
    "BellUp confirmed on H4", "BellUp receives no reversal origin prefix");
Check(verdicts.Analyze(new CandidateEvaluationResult { Ticker = "OLD", PresetScanCode = "CODE",
    PatternVerdictReason = "ReversalHook recognized on D1; Phase=Stalled" }).PatternVerdict == "Match",
    "Old unprefixed recognized rows remain readable");
var dht = JsonSerializer.Deserialize<List<Candle>>(File.ReadAllText(
    Path.Combine(AppContext.BaseDirectory, "Fixtures", "dht-october22-daily.json")))!;
var dhtOrigin = ReversalOriginClassifier.Analyze(dht);
var decision = new DateTime(2026, 10, 22, 17, 6, 26);
Check(dhtOrigin.Kind == "PostBellUpRecovery" && dhtOrigin.RunStart == new DateTime(2026, 10, 15),
    "DHT retains its recent post-BellUp Daily pullback boundary");
var dhtCompletion = ReversalPostBellUpCompletion.Find(dhtOrigin, dht, 24.52m, decision);
Check(dhtCompletion != null && dhtCompletion.Contains("PostBellUpMidCompleted") &&
    dhtCompletion.Contains("Source=CompletedHigh"), "DHT's already reached Daily mid consumes the post-BellUp episode");
var closesOnly = dht.Select(x => new Candle { Time = x.Time, Open = x.Open, Close = x.Close }).ToList();
Check(ReversalPostBellUpCompletion.Find(dhtOrigin, closesOnly, 0m, decision) is { } closedReclaim &&
    closedReclaim.Contains("TouchBar=2026-10-21") && closedReclaim.Contains("Source=CompletedClose"),
    "DHT October21 closed reclaim alone proves completion when highs are unavailable");
var crossingOnly = dht.Where(x => x.Time <= new DateTime(2026, 10, 15)).ToList();
Check(ReversalPostBellUpCompletion.Find(dhtOrigin, crossingOnly, 0m, decision) == null,
    "The initial downward-cross candle cannot consume its own episode");
Check(ReversalPostBellUpCompletion.Find(dhtOrigin, dht, 1m, decision) == dhtCompletion,
    "A later quote pullback cannot revive an already completed mean-return episode");
Check(ReversalPostBellUpCompletion.Find(dhtOrigin with { Kind = "DeepDeclineRecovery" }, dht, 24.52m, decision) == null,
    "Deep recovery can continue beyond Daily mid under its existing admission rules");
Check(ReversalPostBellUpCompletion.Find(dhtOrigin with { Kind = "Unclassified" }, dht, 24.52m, decision) == null,
    "Origin-specific completion does not invent an origin for unclassified setups");
Check(ReversalPostBellUpCompletion.Find(dhtOrigin with { RunStart = new DateTime(2026, 10, 22) },
    dht, 0m, decision) == null, "A prior run's touch cannot blacklist a new independent pullback");
var crossingMid = IbSwingTrader.Application.Dataset.FeatureEngine.CalculateNativeReversalHistory(crossingOnly)[^1].DailyBollingerMidBand;
Check(ReversalPostBellUpCompletion.Find(dhtOrigin, crossingOnly, crossingMid,
    new DateTime(2026, 10, 16, 10, 0, 0))?.Contains("Source=FreshPrice") == true,
    "A fresh current price reaching the last completed Daily mid also consumes the episode");
Check(ReversalPostBellUpCompletion.Find(dhtOrigin, crossingOnly, crossingMid - .0001m,
    new DateTime(2026, 10, 16, 10, 0, 0)) == null, "Do not round a near-touch into completion");
Check(ReversalPostBellUpCompletion.Find(dhtOrigin, dht, 0m, new DateTime(2026, 10, 22, 15, 0, 0)) == null,
    "A forming Daily bar cannot be treated as completed historical evidence");
Console.WriteLine($"Passed {checks} reversal origin checks.");
