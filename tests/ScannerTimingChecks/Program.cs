global using IbSwingTrader.Domain.Market;
using IbSwingTrader.Application.Candidates;
using System.Text.Json;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
var at = new DateTime(2026, 10, 30, 4, 36, 52);
Candle Bar(DateTime t, decimal open = 100m, decimal high = 101m, decimal low = 100m,
    decimal close = 100.5m, decimal volume = 100m) => new() {
    Time = t, Open = open, High = high, Low = low, Close = close, Volume = volume };
var history = Enumerable.Range(0, 6).Select(i => Bar(at.Date.AddDays(-1).AddHours(4*i-4))).ToList();
history[^1] = Bar(at.Date.AddDays(-1).AddHours(16), 100m, 110m, 100m, 109m);
var originalHistory = history.Select(x => (x.Time,x.Open,x.High,x.Low,x.Close)).ToList();
bool Ready(List<Candle> h4, DateTime decision, BellUpTimingObservation? current, out string reason) =>
    BellUpEntryTiming.IsReady([], h4, Timeframe.H4, decision, out reason, out _, current: current);
Check(!Ready(history, at, null, out var oldReason) && oldReason.Contains("range="),
    "Without a current observation the last closed boost still blocks");
var bars = Enumerable.Range(0, 7).Select(i => Bar(at.Date.AddHours(4).AddMinutes(5*i))).ToList();
var current = BellUpEntryTiming.BuildCurrentFromM5(bars, Timeframe.H4, at)!;
Check(current != null && current.Bar.Time == at.Date.AddHours(4) && current.CoveredThrough == at.Date.AddHours(4).AddMinutes(35),
    "Current native H4 uses exactly the completed M5 prefix");
Check(Ready(history, at, current, out var currentReason) && currentReason.Contains("State=Forming") &&
    currentReason.Contains("Source=CompletedM5"),
    "Ordinary current H4 shifts the prior boost into history and records as-of evidence");
var boosting = bars.Select(x => Bar(x.Time, 100m, 102m, 100m, 101.5m)).ToList();
var currentBoost = BellUpEntryTiming.BuildCurrentFromM5(boosting, Timeframe.H4, at)!;
Check(!Ready(history, at, currentBoost, out var boostReason) && boostReason.Contains("RangeRatio=2"),
    "A current green candle at the exact two-times range boundary still blocks");
var red = bars.Select(x => Bar(x.Time, 110m, 111m, 100m, 101m)).ToList();
Check(Ready(history, at, BellUpEntryTiming.BuildCurrentFromM5(red, Timeframe.H4, at), out _),
    "The existing green-candle boost predicate is retained; this does not waive separate decline checks");
var withFuture = bars.Concat([Bar(at.Date.AddHours(4).AddMinutes(35), 100m, 1000m, 1m, 999m),
    Bar(at.Date.AddHours(8), 100m, 1000m, 1m, 999m)]).ToList();
Check(BellUpEntryTiming.BuildCurrentFromM5(withFuture, Timeframe.H4, at)!.Bar.High == 101m,
    "Final forming M5 and future M5 values cannot inflate the current prefix");
var cachedFinalH4 = Bar(at.Date.AddHours(4), 100m, 1000m, 1m, 999m);
Check(Ready([..history,cachedFinalH4], at, current, out _),
    "A cached final H4 is excluded in favour of the explicit as-of M5 observation");
Check(BellUpEntryTiming.GetCompletedCandles([cachedFinalH4], Timeframe.H4, at).Count == 0,
    "Adding current timing does not change shared closed-bar recognition semantics");
Check(!Ready(history, at.AddMinutes(5), current, out var staleReason) && staleReason.Contains("as-of"),
    "Observation identity must match the timing decision");
Check(BellUpEntryTiming.BuildCurrentFromM5(bars.Skip(1), Timeframe.H4, at) == null,
    "Unknown interval open cannot manufacture a current candle");
Check(BellUpEntryTiming.BuildCurrentFromM5(bars.Where((_,i)=>i!=3), Timeframe.H4, at) == null,
    "Missing internal M5 coverage cannot manufacture a narrow current range");
Check(BellUpEntryTiming.BuildCurrentFromM5(bars.Take(6), Timeframe.H4, at) == null,
    "Stale grid tail is not a current observation");
var noTrades = bars.Select(x => Bar(x.Time, volume:0m)).ToList();
Check(BellUpEntryTiming.BuildCurrentFromM5(noTrades, Timeframe.H4, at) == null,
    "An entirely zero-volume grid cannot clear the closed boost veto");
var leadingCarry = bars.Select((x,i) => i == 0 ? Bar(x.Time,100m,1000m,1m,999m,0m) : x).ToList();
Check(BellUpEntryTiming.BuildCurrentFromM5(leadingCarry, Timeframe.H4, at)!.Bar.Open == 100m,
    "Known empty opening slots use the first actual trade as the current candle open");
var withCarry = bars.Select((x,i) => i == 0 ? x : Bar(x.Time,100m,1000m,1m,999m,0m)).ToList();
Check(BellUpEntryTiming.BuildCurrentFromM5(withCarry, Timeframe.H4, at)!.Bar.High == 101m,
    "Zero-volume carry extremes cannot create a traded boost");
Check(BellUpEntryTiming.BuildCurrentFromM5(bars, Timeframe.H4, at.Date.AddHours(20)) == null,
    "No invented native H4 interval after20:00");
Check(BellUpEntryTiming.CurrentStart(Timeframe.D1, at) == null,
    "Premarket does not replace the native regular-session Daily candle");
var dailyAt = at.Date.AddHours(10);
var rth = Enumerable.Range(0,6).Select(i=>Bar(at.Date.AddHours(9.5).AddMinutes(5*i))).ToList();
var currentDaily = BellUpEntryTiming.BuildCurrentFromM5(rth, Timeframe.D1, dailyAt)!;
var dailyHistory = Enumerable.Range(0,6).Select(i=>Bar(at.Date.AddDays(i-6))).ToList();
dailyHistory[^1] = Bar(at.Date.AddDays(-1),100m,110m,100m,109m);
Check(BellUpEntryTiming.IsReady(dailyHistory, [], Timeframe.D1, dailyAt, out _, current:currentDaily),
    "Regular-session Daily timing can use its current M5 observation too");
Check(BellUpEntryTiming.BuildCurrentFromM5(rth, Timeframe.D1, at.Date.AddHours(16)) == null,
    "At16:00 Daily returns to the completed native candle policy");
Check(originalHistory.SequenceEqual(history.Select(x=>(x.Time,x.Open,x.High,x.Low,x.Close))),
    "Native history is never mutated by the timing observation");
using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
    "Fixtures", "cdna-2026-10-30-morning-prefix.json")));
var root = fixture.RootElement;
var cdnaAt = root.GetProperty("DecisionAt").GetDateTime();
var cdnaH4 = JsonSerializer.Deserialize<List<Candle>>(root.GetProperty("H4"))!;
var cdnaM5 = JsonSerializer.Deserialize<List<Candle>>(root.GetProperty("M5"))!;
Check(!Ready(cdnaH4, cdnaAt, null, out _), "Actual CDNA closed-only prefix reproduces Recent boost");
var cdnaCurrent = BellUpEntryTiming.BuildCurrentFromM5(cdnaM5, Timeframe.H4, cdnaAt)!;
Check(Ready(cdnaH4, cdnaAt, cdnaCurrent, out _) && cdnaCurrent.Bar.Open == 81.95m &&
    cdnaCurrent.Bar.Close == 82.55m && cdnaCurrent.Bar.High == 83.8817m && cdnaCurrent.Bar.Low == 81m,
    "Actual CDNA prefix clears this timing veto without final07:50 low or second boost data");
Check(cdnaCurrent.CoveredThrough == new DateTime(2026,10,30,4,35,0) &&
    cdnaM5.All(x=>x.Time.AddMinutes(5)<=cdnaAt), "CDNA fixture contains only pre-decision completed M5");

var chartBars = SessionAlignedH4Builder.Build([
    Bar(new(2026,9,8,8,0,0)), Bar(new(2026,9,8,8,15,0)),
    Bar(new(2026,9,8,9,30,0)), Bar(new(2026,9,8,9,45,0)), Bar(new(2026,9,8,13,30,0))],
    new(2026,9,8,16,0,0));
Check(chartBars.Count == 3 && chartBars[1].Time == new DateTime(2026,9,8,9,30,0),
    "Chart-aligned H4 helper retains its separate session grid");
Console.WriteLine($"Passed {checks} BellUp entry timing checks.");
