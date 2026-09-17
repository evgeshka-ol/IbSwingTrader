using IbSwingTrader.Domain.Candidates;
using IbSwingTrader.Infrastructure.Logging;

var assertions = 0;
void Check(bool value, string message)
{
    assertions++;
    if (!value) throw new InvalidOperationException(message);
}

var scan = new DateTime(2026, 9, 17, 8, 0, 0);
CandidateDetails Candidate(string ticker, string source, string verdict, decimal rank) => new()
{
    Ticker = ticker,
    CandidateSource = source,
    PatternVerdictReason = verdict,
    Scan = new() { ScanTime = scan },
    TradePlan = new(),
    Score = new() { Score = 42m, NextDayRank = rank },
    Diagnostics = new() { EstimatedHitRatePct = 30m },
    Context = new()
};

var fmt = new NumberTextFormatter();
var builder = new CandidateCsvRowBuilder(fmt, new ArrayCellFormatter(fmt), new ObjectPropertyReader());
var old = Candidate("OLD", "Other", "BellUp confirmed on H4", 1000m);
old.Scan.ScanTime = scan.AddDays(-1);
var table = builder.Build(
    [Candidate("REV", "Primary", "", 5m)],
    [Candidate("EMPTY", "Other", "", 900m),
     Candidate("TRI", "Other", "Triangle detected on H4", 800m),
     Candidate("DAILY", "Other", "BellUp confirmed on Daily; Recent boost", 20m),
     Candidate("H4", "DiagnosticRejected", "BellUp confirmed on H4; Not ready", 30m),
     Candidate("RUNLOW", "SameDayContinuation", "BellUp confirmed on H4", 10m),
     Candidate("RUNHIGH", "SameDayContinuation", "BellUp confirmed on Daily", 100m), old],
    new HashSet<string>());

Check(table.Headers.Contains("TotalScore") && !table.Headers.Contains("ScoreScore"), "Rename total score header");
Check(table.Rows.All(x => x["TotalScore"] == "42" && x["DiagnosticsEstimatedHitRatePct"] == "30"),
    "Total score and console confidence remain separate values");
Check(table.Rows.Select(x => x["Ticker"]).SequenceEqual(
    new[] { "RUNHIGH", "RUNLOW", "REV", "H4", "DAILY", "EMPTY", "TRI", "OLD" }),
    "BellUp-first within Other preserves existing ranking and scan/group boundaries");
Check(table.Rows.Single(x => x["Ticker"] == "H4")["DisplayRank"] == "1" &&
    table.Rows.Single(x => x["Ticker"] == "EMPTY")["DisplayRank"] == "3", "Display ranks follow the new ordering");
Check(table.Rows.Single(x => x["Ticker"] == "DAILY")["PatternVerdictReason"] ==
    "BellUp confirmed on Daily; Recent boost", "Preserve concise scanner rejection reason");

Console.WriteLine($"Passed {assertions} candidate CSV checks.");
