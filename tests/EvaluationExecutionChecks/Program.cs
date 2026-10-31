using IBApi;
using IbSwingTrader.Abstractions.Evaluation;
using IbSwingTrader.Abstractions.Logging;
using IbSwingTrader.Abstractions.Market;
using IbSwingTrader.Abstractions.Settings;
using IbSwingTrader.Application.Evaluation;
using IbSwingTrader.Common.Time;
using IbSwingTrader.Domain.Candidates;
using IbSwingTrader.Domain.Dataset;
using IbSwingTrader.Domain.Market;
using IbSwingTrader.Domain.Settings;

var checks = 0;
var start = new DateTime(2026, 7, 15, 10, 0, 0);
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new InvalidOperationException(message);
}
Candle Bar(DateTime t, decimal open, decimal high, decimal low, decimal close, decimal volume = 100m) =>
    new() { Time = t, Open = open, High = high, Low = low, Close = close, Volume = volume };
CandidateDetails Candidate() => new() {
    Ticker = "TEST", CandidateSource = "BellUp", IsBellUpPattern = true,
    Scan = new() { ScanTime = start, ScanTimeZone = MarketTime.DefaultTimeZoneId },
    TradePlan = new() { LiveReferencePrice = 10m, EntryPrice = 10m, ExitPrice = 11m, StopLoss = 9m },
    Score = new(), Context = new()
};
var calendar = new Calendar(start, start.AddMinutes(15));
var history = new History();
var logger = new SilentLogger();
var resolver = new AmbiguousBarResolver(history, logger);
var evaluator = new CandidateEvaluator(new Contracts(), history, resolver, new Settings(), new Verdict(), logger, calendar);
var zero = Bar(start, 10m, 10m, 10m, 10m, 0m);
var realEntry = Bar(start.AddMinutes(5), 10m, 10.5m, 9.9m, 10.2m);
var target = Bar(start.AddMinutes(10), 10.2m, 11.1m, 10.1m, 11m);
history.Bars = [zero, realEntry, target];
var result = (await evaluator.EvaluateAsync([Candidate()])).Single();
Check(result.EntryTime == realEntry.Time && result.Outcome == "Win" && history.Repairs.Count == 0,
    "Zero-volume carry covers the grid but cannot enter; later real entry and target establish Win");
ScannerPriceRebase.Apply(result, 10m);
Check(result.StrategyVersion == 8, "Dataset price normalization cannot downgrade a new evaluation to version7");
history.Bars = [zero, target];
history.RepairBars = [realEntry];
result = (await evaluator.EvaluateAsync([Candidate()])).Single();
Check(result.Outcome == "Win" && result.EntryTime == realEntry.Time &&
    history.Repairs.Last() == (realEntry.Time, realEntry.Time.AddMinutes(5)),
    "Repair only the missing regular M5 range before interpreting price order");
history.RepairBars = [];
result = (await evaluator.EvaluateAsync([Candidate()])).Single();
Check(result.Outcome == "IncompleteHistory" && result.EntryTime == null &&
    result.EvaluationEndTime == realEntry.Time,
    "A missing entry/stop interval prevents later prices from establishing a trade");
history.Bars = [realEntry.WithTime(start), target.WithTime(start.AddMinutes(10))];
result = (await evaluator.EvaluateAsync([Candidate()])).Single();
Check(result.Outcome == "Open" && result.EntryTime == start && result.EvaluationEndTime == start.AddMinutes(5),
    "A confirmed entry preceding a data gap stays unresolved; later target is excluded");
history.Bars = Enumerable.Range(0, 3).Select(i => Bar(start.AddMinutes(5*i), 10m, 10m, 10m, 10m, 0m)).ToList();
result = (await evaluator.EvaluateAsync([Candidate()])).Single();
Check(result.Outcome == "NoEntry" && !result.EntryTouched, "Zero-volume equality cannot create a played plan");

var fullSession = new Calendar(start.Date.AddHours(9.5), start.Date.AddHours(16));
var gaps = EvaluationHistoryCoverage.MissingRegularRanges([], fullSession.BuildSchedule(default, default),
    start.Date, start.Date.AddDays(1));
Check(gaps.Count == 1 && gaps[0].Start == start.Date.AddHours(9.5) && gaps[0].End == start.Date.AddHours(16),
    "An entire missing RTH session yields one bounded 6.5-hour repair");
Check(EvaluationHistoryCoverage.MissingRegularRanges([], fullSession.BuildSchedule(default, default),
    start.Date, start.Date.AddHours(9)).Count == 0, "Premarket horizon does not invent missing future RTH");
var partialEnd = start.AddMinutes(12);
Check(EvaluationHistoryCoverage.MissingRegularRanges([zero, realEntry], calendar.BuildSchedule(default, default),
    start, partialEnd).Count == 0, "An unfinished five-minute slot is outside completed coverage");
var earlyClose = new Calendar(start.Date.AddHours(9.5), start.Date.AddHours(13));
Check(EvaluationHistoryCoverage.MissingRegularRanges([], earlyClose.BuildSchedule(default, default),
    start.Date, start.Date.AddDays(1)).Single().End == start.Date.AddHours(13),
    "Coverage uses the calendar's early-close session boundary");
var holiday = calendar.BuildSchedule(default, default);
holiday.Days[0].IsTradingDay = false;
Check(EvaluationHistoryCoverage.MissingRegularRanges([], holiday, start, start.AddDays(1)).Count == 0,
    "A configured holiday has no missing regular grid");

var parent = Bar(start, 10m, 12m, 8m, 11m); // legacy default Timeframe metadata
history.Minutes = Enumerable.Range(0, 5).Select(i => Bar(start.AddMinutes(i), 10.5m, 10.8m, 10.4m, 10.6m)).ToList();
history.Minutes[0] = Bar(start, 10.5m, 12m, 8m, 11m);
var resolution = await resolver.ResolveLongAsync(Candidate(), new(), parent, 10m, 11m, 9m);
Check(resolution?.ExitBeforeStop == false && resolution.StopTime == start,
    "Both barriers inside one green minute conservatively prefer stop, independent of colour");
history.Minutes[0] = Bar(start, 10.5m, 11.2m, 10.4m, 11m);
resolution = await resolver.ResolveLongAsync(Candidate(), new(), parent, 10m, 11m, 9m);
Check(resolution?.ExitBeforeStop == true,
    "A later ambiguous M5 need not touch entry again before proving its target");
history.Minutes[0].Volume = 0m;
resolution = await resolver.ResolveLongAsync(Candidate(), new(), parent, 10m, 11m, 9m);
Check(resolution == null, "Zero-volume minute cannot resolve a target");
history.Minutes.RemoveAt(4);
Check(await resolver.ResolveLongAsync(Candidate(), new(), parent, 10m, 11m, 9m) == null,
    "Incomplete M1 coverage cannot infer order");
Console.WriteLine($"Passed {checks} evaluation execution checks.");

static class CandleCopy
{
    public static Candle WithTime(this Candle c, DateTime t) => new() {
        Time = t, Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume };
}
sealed class History : IHistoricalDataService
{
    public List<Candle> Bars = [], RepairBars = [], Minutes = [];
    public List<(DateTime,DateTime)> Repairs = [];
    public Task<List<Candle>?> GetCandles(string s, Contract c, Timeframe f, int bars) => Task.FromResult<List<Candle>?>(Bars);
    public Task<List<Candle>?> GetCandlesRange(string s, Contract c, Timeframe f, DateTime from, DateTime until, int maxConsecutiveEmptyChunks = 0) => Task.FromResult<List<Candle>?>(Bars);
    public Task<List<Candle>> GetFreshM5Snapshot(string s, Contract c, DateTime from, DateTime until)
    { Repairs.Add((from, until)); return Task.FromResult(RepairBars); }
    public Task<List<Candle>?> GetM1InsideM5(string s, Contract c, DateTime t) => Task.FromResult<List<Candle>?>(Minutes);
}
sealed class Calendar(DateTime from, DateTime until) : ILocalMarketScheduleProvider
{
    public MarketSessionSchedule BuildSchedule(DateTime startUtc, DateTime endUtc) => new() {
        TimeZoneId = MarketTime.DefaultTimeZoneId,
        Days = [new() { Date = DateOnly.FromDateTime(from), IsTradingDay = true,
            Sessions = [new() { Type = MarketSessionType.Regular,
                StartUtc = MarketTime.ToUtc(from), EndUtc = MarketTime.ToUtc(until) }] }] };
}
sealed class Contracts : IContractResolver
{
    public Task<Contract> ResolveStockAsync(string ticker, TimeSpan? timeout = null, int maxAttempts = 3) => Task.FromResult(new Contract());
}
sealed class Settings : ICandidateEvaluationSettingsProvider
{
    public CandidateEvaluationSettings Get() => new() { ForwardEvaluationDays = 1, FreshDataSafetyLagMinutes = 0 };
}
sealed class Verdict : ICandidatePatternVerdictService
{
    public CandidatePatternVerdict Analyze(CandidateEvaluationResult c) => new("BellUp", "BellUp", "Match", "saved");
    public CandidatePatternVerdict Analyze(EvaluationDatasetRow c) => new("BellUp", "BellUp", "Match", "saved");
}
sealed class SilentLogger : ITextLogger
{
    public void EmptyLine() { }
    public void Info(string m) { }
    public void Debug(string m) { }
    public void Error(string m) { }
    public void Warning(string m) { }
    public void InfoBlock(string t, string m) { }
    public void ErrorBlock(string t, string m) { }
}
