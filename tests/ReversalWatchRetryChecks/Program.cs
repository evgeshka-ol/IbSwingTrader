using IbSwingTrader.Application.WishList;
using IbSwingTrader.Common.Time;
using IbSwingTrader.Domain.Market;

var checks = 0;
var monday = new DateTime(2026, 10, 19);
const string timezone = "America/New_York";
var schedule = new MarketSessionSchedule
{
    Days = [Day(monday), Day(monday.AddDays(1))]
};
var beforeOpen = ReversalWatchPendingChecks.Window(schedule, monday.AddHours(3), timezone)!.Value;
Check(!beforeOpen.Open && beforeOpen.RetryAt == monday.AddHours(4).AddMinutes(5),
    "Pre-session observation waits for the first five-minute window");
Check(beforeOpen.ExpiresAt == monday.AddHours(20), "Observation expires at the eligible trading day's end");
var open = ReversalWatchPendingChecks.Window(schedule, monday.AddHours(4).AddMinutes(2), timezone)!.Value;
Check(open.Open && open.RetryAt == monday.AddHours(4).AddMinutes(5), "Open-session retries use next M5 bucket");
var closed = ReversalWatchPendingChecks.Window(schedule, monday.AddHours(20).AddMinutes(1), timezone)!.Value;
Check(!closed.Open && closed.RetryAt == monday.AddDays(1).AddHours(4).AddMinutes(5), "Next configured session after close");
var sunday = ReversalWatchPendingChecks.Window(schedule, monday.AddDays(-1).AddHours(3), timezone)!.Value;
Check(sunday.RetryAt == monday.AddHours(4).AddMinutes(5), "Weekend does not create a fictitious session");
Check(ReversalWatchPendingChecks.Window(new(), monday, timezone) == null, "No session fails explicitly");
Check(ReversalWatchPendingChecks.NextBucket(monday.AddHours(4).AddMinutes(5)) == monday.AddHours(4).AddMinutes(10),
    "A completed attempt cannot repeat within the same bucket");

var directory = Directory.CreateTempSubdirectory("reversal-watch-retries-");
try
{
    var path = Path.Combine(directory.FullName, "wishlist.pending.json");
    var pending = new ReversalWatchPendingChecks(path);
    await pending.LoadAsync(monday.AddHours(3));
    var stock = new StockInfo { Ticker = "JMKE", ConId = 123, Exchange = "SMART", Currency = "USD", TradingClass = "JMKE" };
    var preset = new PresetScanCode("TOP_PERC_LOSE", "Losers");
    var seen = monday.AddHours(3).AddMinutes(7);
    await pending.DeferAsync(stock, preset, seen, beforeOpen.RetryAt, beforeOpen.ExpiresAt, false, "outside-trading-session");
    Check(pending.Count == 1 && !pending.Due(seen).Any(), "Deferred observation is not due before the market window");
    Check(!File.Exists(Path.Combine(directory.FullName, "wishlist.csv")), "Pending observation is not an active wishlist row");

    // A scan ending before 04:00 must not lose its observation on process restart.
    var restarted = new ReversalWatchPendingChecks(path);
    await restarted.LoadAsync(monday.AddHours(4).AddMinutes(5));
    var restored = restarted.Due(monday.AddHours(4).AddMinutes(5)).Single();
    Check(restored.SeenAt == seen && restored.ToStock().ConId == 123 && restored.ScanCode == preset.ScanCode,
        "Restart preserves sighting and instrument identity");
    var attempt = monday.AddHours(4).AddMinutes(5);
    Check(restarted.BeginAttempt("jmke", attempt), "Due attempt is case insensitive");
    Check(!restarted.BeginAttempt("JMKE", attempt.AddSeconds(1)), "No repeated request in the same M5 bucket");
    await restarted.DeferAsync(stock, preset, restored.SeenAt, ReversalWatchPendingChecks.NextBucket(attempt),
        restored.ExpiresAt, restored.ValidateExisting, "no-fresh-M5-price");
    Check(restarted.Due(attempt).Count() == 0, "Unchanged missing M5 waits for another bucket");
    for (var i = 1; i <= 2; i++)
    {
        var at = attempt.AddMinutes(i * 5);
        Check(restarted.BeginAttempt("JMKE", at), "Bounded later-bucket retry");
        await restarted.DeferAsync(stock, preset, seen, ReversalWatchPendingChecks.NextBucket(at),
            restored.ExpiresAt, false, "no-fresh-M5-price");
    }
    Check(!restarted.Due(attempt.AddMinutes(15)).Any() && !restarted.BeginAttempt("JMKE", attempt.AddMinutes(15)),
        "At most three deferred attempts per ticker per run");
    var nextRun = new ReversalWatchPendingChecks(path);
    await nextRun.LoadAsync(attempt.AddMinutes(15));
    Check(nextRun.Due(attempt.AddMinutes(15)).Single().SeenAt == seen, "New run resets budget but never renews loser sighting");
    await nextRun.CompleteAsync("JMKE");
    await nextRun.LoadAsync(attempt.AddMinutes(15));
    Check(nextRun.Count == 0, "Definitive result removes the pending record durably");
    await nextRun.DeferAsync(stock, preset, seen, beforeOpen.RetryAt, beforeOpen.ExpiresAt, true, "no-fresh-M5-price");
    await nextRun.LoadAsync(attempt);
    Check(nextRun.Due(attempt).Single().ValidateExisting, "Legacy validation retains its non-renewal semantics");
    await nextRun.LoadAsync(monday.AddHours(20));
    Check(nextRun.Count == 0, "Expired sightings cannot authorize enrollment on a later trading day");
    Check(!Directory.EnumerateFiles(directory.FullName).Any(x => x.Contains(".tmp-")), "Atomic saves leave no temporary files");
    await File.WriteAllTextAsync(path, "{");
    var failed = false;
    try { await nextRun.LoadAsync(attempt); }
    catch (System.Text.Json.JsonException) { failed = true; }
    Check(failed, "Corrupt persistence fails rather than silently forgetting pending observations");
}
finally { directory.Delete(true); }
Console.WriteLine($"Passed {checks} reversal watch retry checks.");

TradingDaySchedule Day(DateTime date) => new()
{
    Date = DateOnly.FromDateTime(date), IsTradingDay = true,
    Sessions = [Session(date.AddHours(4), date.AddHours(9.5)),
        Session(date.AddHours(9.5), date.AddHours(16)), Session(date.AddHours(16), date.AddHours(20))]
};
SessionInterval Session(DateTime from, DateTime to) => new()
{
    Scope = MarketSessionScope.TradingHours, StartUtc = MarketTime.ToUtc(from, timezone), EndUtc = MarketTime.ToUtc(to, timezone)
};
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
