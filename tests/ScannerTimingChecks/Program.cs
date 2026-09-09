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
    Check("Daily " + item.Name, Daily(item.Bodies), H4([1m, 1m, 1m]), scan,
        item.Ready, item.Ready ? "" : "Daily", item.Reason);
    Check("H4 " + item.Name, Daily([1m, 1m, 1m]), H4(item.Bodies), scan,
        item.Ready, item.Ready ? "" : "H4", item.Reason);
}

Check("forming H4 boost is excluded", Daily([1m, 1m, 1m]),
    [.. H4([1m, 1m, 1m]), Bar(scan.Date.AddHours(8), 100m)], scan, true);
Check("forming Daily boost is excluded",
    [.. Daily([1m, 1m, 1m]), Bar(scan.Date, 100m)], H4([1m, 1m, 1m]), scan, true);
Check("latest H4 is completed even without a forming bar", Daily([1m, 1m, 1m]),
    H4([1m, 1m, 2m]), scan, false, "H4", "T-1");
Check("H4 becomes eligible for checking exactly at close", Daily([1m, 1m, 1m]),
    [.. H4([1m, 1m, 1m]), Bar(scan.Date.AddHours(8), 2m)],
    scan.Date.AddHours(12), false, "H4", "T-1");
Check("Daily becomes eligible for checking at session close",
    [.. Daily([1m, 1m, 1m]), Bar(scan.Date, 2m)], H4([1m, 1m, 1m]),
    scan.Date.AddHours(16), false, "Daily", "T-1");
Check("prior session retained across holiday gap",
    [Bar(new(2026, 9, 2), 1m), Bar(new(2026, 9, 3), 1m), Bar(new(2026, 9, 4), 2m)],
    H4([1m, 1m, 1m]), scan, false, "Daily", "T-1");

var gapDaily = Daily([1m, 1m, 1m]);
gapDaily[^1].Open = 200m;
gapDaily[^1].Close = 201m;
Check("gap alone is not a body boost", gapDaily, H4([1m, 1m, 1m]), scan, true);
var wickH4 = H4([1m, 1m, 1m]);
wickH4[^1].High = 1000m;
wickH4[^1].Low = 1m;
Check("wicks do not change body timing", Daily([1m, 1m, 1m]), wickH4, scan, true);
Check("input order does not define completion", Daily([1m, 1m, 1m]).AsEnumerable().Reverse(),
    H4([1m, 1m, 2m]).AsEnumerable().Reverse(), scan, false, "H4", "T-1");

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

void Check(string name, IEnumerable<Candle> daily, IEnumerable<Candle> h4,
    DateTime time, bool expected, params string[] reasonParts)
{
    var actual = BellUpEntryTiming.IsReady(daily, h4, time, out var reason);
    if (actual != expected || reasonParts.Any(part => !reason.Contains(part, StringComparison.Ordinal)))
        throw new InvalidOperationException($"{name}: expected ready={expected}, got {actual}; {reason}");
    checks++;
}
