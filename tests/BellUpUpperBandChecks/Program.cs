global using IbSwingTrader.Domain.Market;
using IbSwingTrader.Application.Candidates;

var checks = 0;
void Check(bool value, string message)
{
    checks++;
    if (!value) throw new InvalidOperationException(message);
}
List<Candle> Bars(decimal[] closes) => closes.Select((close, i) => new Candle
{
    Time = new DateTime(2026, 10, 5, 8, 0, 0).AddHours(4 * i),
    Open = close, High = close, Low = close, Close = close
}).ToList();
bool Guard(decimal[] closes, decimal[] upper, decimal[] mid) =>
    BellUpUpperBandSlowdown.IsPause(Bars(closes), upper, mid);
decimal[] close = [10.82m, 12.56m, 12.45m, 12.43m];
decimal[] upper = [11.04938m, 11.67612m, 12.01106m, 12.27346m];
decimal[] mid = [10.723m, 10.8105m, 10.888m, 10.9645m];
Check(Guard(close, upper, mid), "NVAX native H4 pre-fall geometry");
Check(Guard([71.57m, 76.28m, 79.16m, 78.2m],
    [69.7917m, 73.38524m, 76.78478m, 79.31836m],
    [56.2455m, 57.5015m, 59.13m, 60.665m]), "PENG completed D1 pre-fall geometry");
Check(!Guard([10.82m, 12.56m, 12.45m, 12.60m], upper, mid), "Upper slowing alone cannot reject advancing price");
Check(!Guard(close, [11m, 11.6m, 12.2m, 12.4m], mid), "Single slowdown is insufficient");
Check(!Guard(close, [11m, 11.6m, 11.9m, 12.3m], mid), "Renewed expansion clears the warning");
Check(!Guard([12.6m, 12.5m, 12.4m, 12.3m], upper, mid), "Require preceding rise");
Check(!Guard([10.82m, 12.56m, 12.45m, 10.9m], upper, mid), "Below mid belongs to other structural guards");
Check(!Guard(close, upper, [10.7m, 10.8m, 11.1m, 11m]), "Falling mid is outside this shape");
Check(Guard(close.Select(x => x * 100m).ToArray(), upper.Select(x => x * 100m).ToArray(),
    mid.Select(x => x * 100m).ToArray()), "Scale invariant");
Check(!Guard(close, [11m, 0m, 12m, 12.1m], mid), "Invalid bands");
Check(!Guard(close, [11m], mid), "Unaligned rows");
var native = Bars(close);
native[^1].Time = new DateTime(2026, 10, 6, 4, 0, 0);
var before = BellUpEntryTiming.GetCompletedCandles(native, Timeframe.H4, new DateTime(2026, 10, 6, 7, 59, 59));
Check(!BellUpUpperBandSlowdown.IsPause(before, upper.Take(3).ToArray(), mid.Take(3).ToArray()),
    "04:00 H4 cannot confirm before 08:00");
Check(BellUpUpperBandSlowdown.IsPause(BellUpEntryTiming.GetCompletedCandles(native, Timeframe.H4,
    new DateTime(2026, 10, 6, 8, 0, 0)), upper, mid), "Available at H4 completion");
var daily = Bars(close);
for (var i = 0; i < daily.Count; i++) daily[i].Time = new DateTime(2026, 10, 9).AddDays(i);
Check(BellUpEntryTiming.GetCompletedCandles(daily, Timeframe.D1, daily[^1].Time.AddHours(15)).Count == 3,
    "Unfinished D1 excluded");
Console.WriteLine($"Passed {checks} upper-band pause diagnostic checks.");
