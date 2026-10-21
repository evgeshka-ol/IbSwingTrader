using IBApi;
using IbSwingTrader.Abstractions.Logging;
using IbSwingTrader.Abstractions.Market;
using IbSwingTrader.Application.Candidates;
using IbSwingTrader.Application.Market;
using IbSwingTrader.Domain.Market;

var checks = 0;
var monday = new DateTime(2026, 10, 19);
var thursday = monday.AddDays(-4);
var friday = monday.AddDays(-3);
var provider = new DailyProvider([Bar(thursday, 10m), Bar(friday, 9m), Bar(monday, 8m)]);
var cache = new MemoryCache([Bar(thursday, 10m), Bar(friday, 9m)]);
var service = new HistoricalDataService(provider, new Throttler(), cache, new Retry(),
    new SilentLogger(), new Gaps(), new Coverage());
var contract = new Contract { Symbol = "OCUL", SecType = "STK", Exchange = "SMART", Currency = "USD" };
var before = await service.GetCandlesRange("OCUL", contract, Timeframe.D1, thursday.AddHours(12), monday.AddHours(15).AddMinutes(59));
Check(provider.Requests.Last().End == monday, "Before close the daily request excludes a forming same-day bar");
Check(before![^1].Time == friday, "Pre-close history remains at Friday");
var after = await service.GetCandlesRange("OCUL", contract, Timeframe.D1, thursday.AddHours(12), monday.AddHours(16).AddMinutes(15));
Check(provider.Requests.Last().End == monday.AddHours(16).AddMinutes(15), "After close actual request time is preserved");
Check(after![^1].Time == monday, "Aftermarket receives Monday's completed daily bar");
Check(cache.Bars.Any(x => x.Time == monday), "Completed Monday candle reaches native D1 cache");
Check(BellUpEntryTiming.GetCompletedCandles(after, Timeframe.D1, monday.AddHours(16).AddMinutes(15))[^1].Time == monday,
    "Watch freshness gate can now match the expected closed date");
Check(provider.Requests.All(x => x.Start.TimeOfDay == TimeSpan.Zero), "Daily range start still includes the complete first date");
var midnight = await service.GetCandlesRange("OCUL", contract, Timeframe.D1, thursday, monday.AddDays(1));
Check(midnight![^1].Time == monday, "Explicit next-day midnight end remains a valid historical boundary");
Check(BellUpEntryTiming.GetCompletedCandles(after, Timeframe.D1, monday.AddHours(15))[^1].Time == friday,
    "Completion filter still prevents a later cache bar from leaking into a pre-close decision");
Console.WriteLine($"Passed {checks} daily history checks.");

Candle Bar(DateTime date, decimal close) => new()
    { Timeframe = Timeframe.D1, Time = date, Open = close, High = close, Low = close, Close = close };
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
sealed class DailyProvider(List<Candle> bars) : IMarketDataProvider
{
    public List<(DateTime Start, DateTime End)> Requests { get; } = [];
    public Task<List<Candle>> GetHistoricalRange(Contract contract, Timeframe frame, DateTime start, DateTime end)
    {
        Requests.Add((start, end));
        // Match the broker boundary: a daily bar is unavailable to an end at its own date's midnight.
        return Task.FromResult(bars.Where(x => x.Time >= start && x.Time.Date.AddHours(16) <= end).ToList());
    }
    public Task<List<Candle>> GetCandles(Contract contract, Timeframe frame, DateTime end, int count) => throw new NotSupportedException();
}
sealed class MemoryCache(List<Candle> initial) : IHistoricalCache
{
    public List<Candle> Bars { get; private set; } = initial;
    public bool TryLoad(string symbol, Timeframe frame, out List<Candle>? bars) { bars = Bars.ToList(); return bars.Count > 0; }
    public void Save(string symbol, Timeframe frame, List<Candle> bars) => Bars = bars;
}
sealed class Throttler : IHistoricalRequestThrottler
{
    public Task<IDisposable> AcquireAsync() => Task.FromResult<IDisposable>(new Lease());
    sealed class Lease : IDisposable { public void Dispose() { } }
}
sealed class Retry : IHistoricalRetryPolicy
{
    public Task<T?> ExecuteAsync<T>(Func<Task<T?>> action, int retries = 2) => action();
}
sealed class Coverage : IMarketCoverageService
{
    public Task<bool> HasExpectedBarsBetweenAsync(Contract c, Timeframe f, DateTime from, DateTime to, CancellationToken token = default)
        => Task.FromResult(true);
}
sealed class Gaps : IMarketGapAnalyzer
{
    public Task<bool> IsExpectedGapAsync(Contract c, Timeframe f, DateTime from, DateTime to, CancellationToken token = default)
        => Task.FromResult(true);
    public Task<DateTime?> GetNextExpectedBarTimeAsync(Contract c, Timeframe f, DateTime from, CancellationToken token = default)
        => Task.FromResult<DateTime?>(null);
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
