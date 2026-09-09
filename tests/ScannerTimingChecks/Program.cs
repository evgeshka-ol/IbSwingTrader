global using IbSwingTrader.Domain.Market;
using IbSwingTrader.Application.Candidates;

var scan = new DateTime(2026, 9, 9, 11, 0, 0);
var checks = 0;
var cases = new (string Name, decimal[] Bodies, bool Ready, string Reason)[]
{
    ("no boost", [1m, 1m, 1m], true, ""),
    ("exactly twice on T-1", [1m, 1m, 2m], false, "T-1"),
    ("more than twice on T-1", [1m, 1m, 3m], false, "T-1"),
    ("just below twice", [1m, 1m, 1.99m], true, ""),
    ("boost on T-2 after red T-3", [-1m, 2m, 1m], false, "T-2"),
    ("boost on T-2 despite red T-1", [1m, 2m, -3m], false, "T-2"),
    ("red T-1 is not a boost", [1m, 1m, -3m], true, ""),
    ("red T-2 is not a boost", [1m, -3m, 1m], true, ""),
    ("compare with absolute red body", [1m, -2m, 3m], true, ""),
    ("twice the absolute red body", [1m, -2m, 4m], false, "T-1"),
    ("green after doji", [1m, 0m, 0.01m], false, "T-1"),
    ("doji is not green", [1m, 0m, 0m], true, ""),
    ("boost older than T-2", [1m, 3m, 1m, 1m], true, ""),
    ("two completed candles are insufficient", [1m, 1m], false, "three completed"),
    ("empty history", [], false, "three completed")
};

foreach (var item in cases)
{
    Check("Daily " + item.Name, Timeframe.D1, Daily(item.Bodies), H4([1m, 1m, 1m]), scan,
        item.Ready, item.Ready ? "" : "Daily", item.Reason);
    Check("H4 " + item.Name, Timeframe.H4, Daily([1m, 1m, 1m]), H4(item.Bodies), scan,
        item.Ready, item.Ready ? "" : "H4", item.Reason);
}

Check("forming H4 boost is excluded", Timeframe.H4, Daily([1m, 1m, 1m]),
    [.. H4([1m, 1m, 1m]), Bar(scan.Date.AddHours(8), 100m)], scan, true);
Check("forming Daily boost is excluded", Timeframe.D1,
    [.. Daily([1m, 1m, 1m]), Bar(scan.Date, 100m)], H4([1m, 1m, 1m]), scan, true);
Check("latest H4 is completed even without a forming bar", Timeframe.H4, Daily([1m, 1m, 1m]),
    H4([1m, 1m, 2m]), scan, false, "H4", "T-1");
Check("H4 becomes eligible for checking exactly at close", Timeframe.H4, Daily([1m, 1m, 1m]),
    [.. H4([1m, 1m, 1m]), Bar(scan.Date.AddHours(8), 2m)],
    scan.Date.AddHours(12), false, "H4", "T-1");
Check("Daily becomes eligible for checking at session close", Timeframe.D1,
    [.. Daily([1m, 1m, 1m]), Bar(scan.Date, 2m)], H4([1m, 1m, 1m]),
    scan.Date.AddHours(16), false, "Daily", "T-1");
Check("prior session retained across holiday gap", Timeframe.D1,
    [Bar(new(2026, 9, 2), 1m), Bar(new(2026, 9, 3), 1m), Bar(new(2026, 9, 4), 2m)],
    H4([1m, 1m, 1m]), scan, false, "Daily", "T-1");

var gapDaily = Daily([1m, 1m, 1m]);
gapDaily[^1].Open = 200m;
gapDaily[^1].Close = 201m;
Check("gap alone is not a body boost", Timeframe.D1, gapDaily, H4([1m, 1m, 1m]), scan, true);
var wickH4 = H4([1m, 1m, 1m]);
wickH4[^1].High = 1000m;
wickH4[^1].Low = 1m;
Check("wicks do not change body timing", Timeframe.H4, Daily([1m, 1m, 1m]), wickH4, scan, true);
Check("input order does not define completion", Timeframe.H4, Daily([1m, 1m, 1m]).AsEnumerable().Reverse(),
    H4([1m, 1m, 2m]).AsEnumerable().Reverse(), scan, false, "H4", "T-1");

Check("Daily boost does not veto H4 entry", Timeframe.H4,
    Daily([1m, 3m, 1m]), H4([1m, 1m, 1m]), scan, true);
Check("H4 boost does not veto Daily body timing", Timeframe.D1,
    Daily([1m, 1m, 1m]), H4([1m, 3m, 1m]), scan, true);
Check("missing Daily does not veto H4 entry", Timeframe.H4,
    [], H4([1m, 1m, 1m]), scan, true);
Check("missing H4 does not veto Daily body timing", Timeframe.D1,
    Daily([1m, 1m, 1m]), [], scan, true);
Check("unsupported timeframe cannot pass", Timeframe.W1,
    Daily([1m, 1m, 1m]), H4([1m, 1m, 1m]), scan, false, "confirmed Daily or H4");

var chartBars = SessionAlignedH4Builder.Build(
    [
        Bar(new(2026, 9, 8, 8, 0, 0), 0.01m),
        Bar(new(2026, 9, 8, 8, 15, 0), 0.01m),
        Bar(new(2026, 9, 8, 9, 30, 0), 0.41m),
        Bar(new(2026, 9, 8, 9, 45, 0), -0.05m),
        Bar(new(2026, 9, 8, 13, 30, 0), 0.38m)
    ], new(2026, 9, 8, 16, 0, 0));
if (chartBars.Count != 3 ||
    chartBars[0].Time != new DateTime(2026, 9, 8, 8, 0, 0) ||
    chartBars[1].Time != new DateTime(2026, 9, 8, 9, 30, 0) ||
    chartBars[2].Time != new DateTime(2026, 9, 8, 13, 30, 0) ||
    chartBars[1].Open != 100m || chartBars[1].Close != 99.95m)
{
    throw new InvalidOperationException("Session-aligned H4 buckets do not match the 08:00/09:30 chart grid.");
}
checks++;

Console.WriteLine($"Passed {checks} BellUp entry timing checks.");

List<Candle> Daily(decimal[] bodies) => bodies
    .Select((body, index) => Bar(scan.Date.AddDays(index - bodies.Length), body)).ToList();

List<Candle> H4(decimal[] bodies) => bodies
    .Select((body, index) => Bar(scan.Date.AddHours(4 + (index - bodies.Length + 1) * 4), body)).ToList();

static Candle Bar(DateTime time, decimal body) => new()
{
    Time = time,
    Open = 100m,
    Close = 100m + body,
    High = 100m + Math.Max(body, 0m),
    Low = 100m + Math.Min(body, 0m)
};

void Check(string name, Timeframe timeframe, IEnumerable<Candle> daily, IEnumerable<Candle> h4,
    DateTime time, bool expected, params string[] reasonParts)
{
    var actual = BellUpEntryTiming.IsReady(daily, h4, timeframe, time, out var reason);
    if (actual != expected || reasonParts.Any(part => !reason.Contains(part, StringComparison.Ordinal)))
        throw new InvalidOperationException($"{name}: expected ready={expected}, got {actual}; {reason}");
    checks++;
}
