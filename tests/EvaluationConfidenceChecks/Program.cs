using IbSwingTrader.Abstractions.Evaluation;
using IbSwingTrader.Abstractions.Logging;
using IbSwingTrader.Abstractions.Market;
using IbSwingTrader.Abstractions.Settings;
using IbSwingTrader.Application.Dataset;
using IbSwingTrader.Application.Evaluation;
using IbSwingTrader.Domain.Candidates;
using IbSwingTrader.Domain.Dataset;
using IbSwingTrader.Domain.Market;
using IbSwingTrader.Domain.Settings;
using IbSwingTrader.Infrastructure.Logging;
using IbSwingTrader.Infrastructure.Settings;

var start = new DateTime(2026, 7, 15, 10, 0, 0);
var checks = 0;
var row = Row();
EvaluationConfidenceMetrics.ScoreForecast(row);
Check(row.BrierLoss == 0.04m, "80% Win");
row.Outcome = "Loss";
EvaluationConfidenceMetrics.ScoreForecast(row);
Check(row.BrierLoss == 0.64m, "80% Loss");
row.Outcome = "Open";
EvaluationConfidenceMetrics.ScoreForecast(row);
Check(row.BrierLoss == null && row.ConfidenceEvaluationStatus == "Unresolved", "Open is censored");
row.EntryTime = null;
EvaluationConfidenceMetrics.ScoreForecast(row);
Check(row.BrierLoss == null && row.ConfidenceEvaluationStatus == "NoEntry", "NoEntry is not Loss");
row = Row();
row.DiagnosticsEstimatedHitRateScope = "";
EvaluationConfidenceMetrics.ScoreForecast(row);
Check(row.BrierLoss == null && row.ConfidenceEvaluationStatus == "UnsupportedScope", "Legacy amplitude is not Win");
row.DiagnosticsEstimatedHitRateScope = "WinGivenEntryAndResolution";
row.DiagnosticsEstimatedHitRateModel = "";
EvaluationConfidenceMetrics.ScoreForecast(row);
Check(row.BrierLoss == null, "Missing model must not acquire current semantics");
row = Row();
row.DiagnosticsEstimatedHitRatePct = 101;
EvaluationConfidenceMetrics.ScoreForecast(row);
Check(row.ConfidenceEvaluationStatus == "InvalidForecast", "Probability bounds");
row.DiagnosticsEstimatedHitRatePct = 0;
EvaluationConfidenceMetrics.ScoreForecast(row);
Check(row.BrierLoss == 1m, "Zero is a forecast, not missing");

row = Row();
row.Outcome = "Loss";
var bars = new List<Candle>
{
    Bar(start, 10, 10.2m, 9.9m, 10.1m),
    Bar(start.AddMinutes(5), 10.1m, 10.9m, 10, 10.5m),
    Bar(start.AddMinutes(10), 10.5m, 15, 8.9m, 9.1m),
    Bar(start.AddMinutes(15), 9.1m, 20, 9, 19)
};
EvaluationConfidenceMetrics.MeasureProgress(row, start.AddHours(1), start.AddMinutes(10), bars, []);
Check(row.TargetProgressPct == 90m && row.TradeMfePct == 9m, "Exclude terminal high and post-stop rally");
Check(row.TargetProgressStatus == "FinalConservative", "Unknown terminal order is explicit");
Check(row.Outcome == "Loss", "Progress never rewrites outcome");

var minuteBars = Enumerable.Range(0, 5)
    .Select(i => Bar(start.AddMinutes(10 + i), 10.5m, 10.6m, 10.4m, 10.5m)).ToList();
minuteBars[0] = Bar(start.AddMinutes(10), 10.5m, 10.95m, 10.4m, 10.6m);
minuteBars[1] = Bar(start.AddMinutes(11), 10.6m, 15, 8.9m, 9.1m);
EvaluationConfidenceMetrics.MeasureProgress(row, start.AddHours(1), start.AddMinutes(11), bars, minuteBars);
Check(row.TargetProgressPct == 95m, "M1 preserves proven progress before stop minute");
minuteBars.RemoveAt(4);
EvaluationConfidenceMetrics.MeasureProgress(row, start.AddHours(1), start.AddMinutes(11), bars, minuteBars);
Check(row.TargetProgressPct == 90m, "Incomplete M1 cannot refine parent");

row = Row();
row.Outcome = "Open";
bars = [Bar(start, 10.8m, 12, 9.9m, 10.1m), Bar(start.AddMinutes(5), 10.1m, 10.6m, 10, 10.4m)];
EvaluationConfidenceMetrics.MeasureProgress(row, start.AddMinutes(10), null, bars, []);
Check(row.TargetProgressPct == 60m && row.TargetProgressStatus == "ProvisionalConservative",
    "Pre-entry high is excluded, Open is provisional");
bars.Add(Bar(start.AddMinutes(10), 10.4m, 30, 10, 25));
EvaluationConfidenceMetrics.MeasureProgress(row, start.AddMinutes(10), null, bars, []);
Check(row.TargetProgressPct == 60m, "Unfinished/future parent cannot inflate progress");
EvaluationConfidenceMetrics.MeasureProgress(row, start.AddMinutes(10), null, [], []);
Check(row.TargetProgressPct == null && row.TargetProgressStatus == "InsufficientData", "Missing data is not zero");
row = Row();
EvaluationConfidenceMetrics.MeasureProgress(row, start.AddHours(1), null, [], []);
Check(row.TargetProgressPct == 100m && row.TradeMfePct == 10m && row.TargetProgressStatus == "FinalFromOutcome",
    "Saved target fill establishes Win progress even after cache eviction");

var directory = Directory.CreateTempSubdirectory("confidence-checks-");
try
{
    var fmt = new NumberTextFormatter();
    var props = new ObjectPropertyReader();
    var csv = new EvaluationDatasetCsvService(fmt, new ArrayCellFormatter(fmt), props);
    var output = Path.Combine(directory.FullName, "datasets", "evaluation-dataset.csv");
    row = Row();
    row.DiagnosticsEstimatedHitRatePct = 80.1m;
    EvaluationConfidenceMetrics.ScoreForecast(row);
    await csv.WriteAsync(output, [row]);
    var headers = (await File.ReadAllLinesAsync(output))[0].TrimStart('\uFEFF').Split(',');
    var block = new[] { "DiagnosticsEstimatedHitRatePct", "DiagnosticsEstimatedHitRateModel",
        "DiagnosticsEstimatedHitRateScope", "BrierLoss", "ConfidenceEvaluationStatus",
        "TradeMfePct", "TargetProgressPct", "TargetProgressStatus" };
    Check(headers.Skip(Array.IndexOf(headers, "PlannedLossPct") + 1).Take(block.Length).SequenceEqual(block),
        "Feedback columns immediately follow PlannedLossPct");
    Check((await csv.ReadAsync(output)).Single().BrierLoss == 0.039601m, "CSV preserves Brier precision");
    await File.WriteAllTextAsync(output, "Ticker,PresetScanCode,ScanTime,Outcome\nOLD,CODE,2026-07-15 10:00:00,Loss\n");
    var legacy = (await csv.ReadAsync(output)).Single();
    Check(legacy.BrierLoss == null && legacy.DiagnosticsEstimatedHitRatePct == null, "Legacy CSV remains readable");

    // Backfill a dataset-only historical result, outside every normal report filter.
    row = Row();
    row.DiagnosticsEstimatedHitRatePct = null;
    row.DiagnosticsEstimatedHitRateModel = row.DiagnosticsEstimatedHitRateScope = "";
    row.ScanPrice = 11m;
    row.MaxPrice = 12m;
    row.MinPrice = 8m;
    row.MaxTime = start.AddHours(1);
    row.MinTime = start;
    row.DaysToMaxUpFromScan = 0;
    row.AmplitudePct = 50m;
    row.PatternVerdictReason = "PostBellUpRecovery; ReversalHook confirmed on H4";
    await csv.WriteAsync(output, [row]);
    var candidate = new CandidateDetails
    {
        Ticker = row.Ticker, CandidateSource = "BellUp",
        Scan = new() { ScanTime = row.ScanTime, PresetScanCode = row.PresetScanCode },
        TradePlan = new() { EntryPrice = 10m, ExitPrice = 11m, StopLoss = 9m },
        Diagnostics = new() { EstimatedHitRatePct = 80.1m, EstimatedHitRateModel = "saved-v1",
            EstimatedHitRateScope = "WinGivenEntryAndResolution" }, Score = new(), Context = new()
    };
    var paths = new AgentPathService(new AgentSettingsProvider(directory.FullName));
    var evaluationCsv = new CandidateEvaluationCsvService(paths, new SilentLogger());
    var evaluationPath = Path.Combine(directory.FullName, "forecast-evaluation.csv");
    await evaluationCsv.WriteAsync(evaluationPath, [new CandidateEvaluationResult
    {
        Ticker = "TEST", PresetScanCode = "CODE", ScanTime = start,
        EvaluatedAt = start.AddHours(1), DiagnosticsEstimatedHitRatePct = 80.1m,
        DiagnosticsEstimatedHitRateModel = "saved-v1", DiagnosticsEstimatedHitRateScope = "WinGivenEntryAndResolution",
        PatternVerdictReason = "DeepDeclineRecovery; ReversalHook confirmed on H4"
    }]);
    var savedEvaluation = (await evaluationCsv.ReadAsync(evaluationPath)).Single();
    Check(savedEvaluation.PatternVerdictReason == "DeepDeclineRecovery; ReversalHook confirmed on H4",
        "Evaluation CSV retains the origin-prefixed verdict");
    Check(savedEvaluation.DiagnosticsEstimatedHitRatePct == 80.1m &&
        savedEvaluation.DiagnosticsEstimatedHitRateModel == "saved-v1" &&
        savedEvaluation.DiagnosticsEstimatedHitRateScope == "WinGivenEntryAndResolution",
        "Evaluations retain original forecast metadata for later snapshot-independent rebuilds");
    var builder = new EvaluationDatasetBuilder(new EmptyEvaluations(), csv, new SavedCandidates(candidate),
        new EmptyCache(), null!, paths, new Settings(), new Verdict(), new SilentLogger());
    await builder.RefreshFeedbackAsync();
    var refreshed = (await csv.ReadAsync(output)).Single();
    Check(refreshed.PatternVerdictReason == "PostBellUpRecovery; ReversalHook confirmed on H4",
        "Historical refresh preserves the saved origin prefix without reconstructing from later cache");
    Check(refreshed.BrierLoss == 0.039601m && refreshed.Outcome == "Win" && refreshed.EntryTime == row.EntryTime,
        "Offline feedback refresh works without source evaluations and preserves the saved trade");
    Check(refreshed.AmplitudePct == 20m && refreshed.GroupLabel == "TradeCandidate",
        "Historical refresh rebases entered amplitude from minimum to entry and updates its label");
    await builder.RefreshFeedbackAsync();
    Check((await csv.ReadAsync(output)).Single().AmplitudePct == 20m,
        "Repeated historical refresh does not compound amplitude rebasing");
    await builder.RunAsync();
    var backfilled = (await csv.ReadAsync(output)).Single();
    Check(backfilled.DiagnosticsEstimatedHitRatePct == 80.1m && backfilled.BrierLoss == 0.039601m,
        "Backfill saved forecast without rerunning evaluation");
    Check(backfilled.AmplitudePct == 20m, "Full rebuild retains the entry-based amplitude");
    Check(backfilled.ScanTime == start.AddMinutes(-5) && backfilled.TargetProgressPct == 100m,
        "Historical forecast survives time/amplitude filters and derives Win progress");
    await builder.UpsertAsync([]);
    Check((await csv.ReadAsync(output)).Single().BrierLoss == 0.039601m,
        "Incremental merge also retains historical confidence feedback");
    Check((await csv.ReadAsync(output)).Single().AmplitudePct == 20m,
        "Incremental merge retains the entry-based amplitude");
}
finally { directory.Delete(true); }
Console.WriteLine($"Passed {checks} evaluation confidence checks.");

EvaluationDatasetRow Row() => new()
{
    Ticker = "TEST", PresetScanCode = "CODE", GroupLabel = "", CandidateGroup = "BellUp",
    ScanTime = start.AddMinutes(-5), EntryTime = start, EntryPrice = 10, ExitPrice = 11, StopLoss = 9,
    Outcome = "Win", EvaluatedAt = start.AddHours(1), DiagnosticsEstimatedHitRatePct = 80,
    DiagnosticsEstimatedHitRateModel = "saved-v1", DiagnosticsEstimatedHitRateScope = "WinGivenEntryAndResolution"
};
Candle Bar(DateTime time, decimal open, decimal high, decimal low, decimal close) =>
    new() { Time = time, Open = open, High = high, Low = low, Close = close };
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}

sealed class AgentSettingsProvider(string root) : IAgentSettingsProvider
{
    public AgentSettings Get() => new() { Paths = new() { DataRoot = root } };
}
sealed class Settings : IBuildEvaluationDatasetSettingsProvider
{
    public BuildEvaluationDatasetSettings Get() => new()
        { MinScanTime = new DateTime(2099, 1, 1), RecentScanDays = 1, MinAmplitudePct = 99 };
}
sealed class EmptyEvaluations : ICandidateEvaluationCsvService
{
    public Task<List<CandidateEvaluationResult>> ReadAsync(string path) => Task.FromResult(new List<CandidateEvaluationResult>());
    public Task WriteAsync(string path, List<CandidateEvaluationResult> results) => throw new NotSupportedException();
}
sealed class SavedCandidates(CandidateDetails candidate) : ICandidateFileService
{
    public Task<CandidateFileDocument> ReadAsync(string path) => Task.FromResult(new CandidateFileDocument { SameDayCandidates = [candidate] });
    public Task WriteAsync(string path, CandidateFileDocument doc, IEnumerable<CandidateDetails> current) => throw new NotSupportedException();
    public Task NormalizeAsync(string path) => throw new NotSupportedException();
}
sealed class EmptyCache : IHistoricalCache
{
    public bool TryLoad(string symbol, Timeframe timeframe, out List<Candle>? candles) { candles = null; return false; }
    public void Save(string symbol, Timeframe timeframe, List<Candle> candles) => throw new NotSupportedException();
}
sealed class Verdict : ICandidatePatternVerdictService
{
    public CandidatePatternVerdict Analyze(CandidateEvaluationResult row) => new("BellUp", "BellUp", "Match", "saved");
    public CandidatePatternVerdict Analyze(EvaluationDatasetRow row) => new("BellUp", "BellUp", "Match", "saved");
}
sealed class SilentLogger : ITextLogger
{
    public void EmptyLine() { }
    public void Info(string message) { }
    public void Debug(string message) { }
    public void Error(string message) { }
    public void InfoBlock(string title, string block) { }
    public void ErrorBlock(string title, string block) { }
    public void Warning(string message) { }
}
