global using IbSwingTrader.Domain.Market;
global using IbSwingTrader.Domain.Candidates;
using IbSwingTrader.Application.Candidates;

var now = new DateTime(2026, 9, 21, 5, 3, 21);
var previous = Bar(now.Date.AddHours(4).AddMinutes(55), 162.72m, 162.95m);
var current = Bar(now.Date.AddHours(5), 162.95m, 163.02m);
var future = Bar(now.Date.AddHours(5).AddMinutes(5), 180m, 200m);
var snapshot = ExecutionPriceSnapshot.FromM5([future, previous, current], now, now.AddSeconds(2))!;
Check(snapshot.Price == 163.02m, "Fresh partial close is the execution reference");
Check(snapshot.ProjectedEntryPrice == 163.18m, "User's MSTR formula uses current open and previous body");
Check(snapshot.PriceTime == now && snapshot.ObservedAt == now.AddSeconds(2), "Request and receipt timestamps remain distinct");
Check(snapshot.Source == "M5PartialClose", "Partial source is explicit");
current.Close = 170m;
Check(snapshot.ProjectedEntryPrice == 163.18m, "Forecast does not use the forming close");
var completedOnly = ExecutionPriceSnapshot.FromM5([previous], now, now)!;
Check(completedOnly.Price == 162.95m && completedOnly.PriceTime == now.Date.AddHours(5), "Completed fallback carries actual close time");
Check(completedOnly.ProjectedEntryPrice == null, "Never invent the current opening price");
Check(ExecutionPriceSnapshot.FromM5([Bar(now.AddDays(-1), 100m, 100m)], now, now) == null, "Old session cannot supply a fresh quote");
Check(ExecutionPriceSnapshot.FromM5([future], now, now) == null, "Future bars are excluded");
Check(ExecutionPriceSnapshot.FromM5([current], now, now)!.ProjectedEntryPrice == null, "Missing previous bar cannot supply a forecast");
previous.Close = 162m;
Check(ExecutionPriceSnapshot.FromM5([previous, current], now, now)!.ProjectedEntryPrice == null, "Red body is not an upward impulse forecast");
var plan = new TradePlanInfo { EntryPrice = 163.02m };
snapshot.ApplyTo(plan);
Check(plan.EntryPrice == 163.02m && plan.M5CurrentOpen == 162.95m, "Diagnostics do not override the executable entry");
Console.WriteLine("Execution timing checks passed.");

static Candle Bar(DateTime time, decimal open, decimal close) => new()
{
    Timeframe = Timeframe.M5, Time = time, Open = open, Close = close,
    High = Math.Max(open, close), Low = Math.Min(open, close)
};
static void Check(bool passed, string message)
{
    if (!passed) throw new InvalidOperationException(message);
}
