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
Console.WriteLine($"Passed {checks} reversal origin checks.");
