global using IbSwingTrader.Domain.Market;
using IbSwingTrader.Application.Candidates;

var assertions = 0;
void Check(bool value, string message)
{
    assertions++;
    if (!value) throw new InvalidOperationException(message);
}

List<Candle> Bodies(params decimal[] bodies) => bodies.Select((body, i) => new Candle
{
    Time = new DateTime(2026, 9, 8, 0, 0, 0).AddHours(i * 4),
    Open = 13m, Close = 13m + body, High = 20m, Low = 1m
}).ToList();

var bars = Bodies(-0.04m, 0.11m, 0.01m, 0.16m, -0.02m, 0.01m);
var flags = Enumerable.Repeat(true, bars.Count).ToArray();
Check(BellUpBoostExit.TryCalculate(bars, flags, 13.37m, out var result, out _), "Two separated boosts found");
Check(result.AverageBody == 0.135m && result.ExitPrice == 13.51m, "Mean body and away-from-zero price rounding");
Check(result.LatestBoost == bars[3] && result.EarlierBoost == bars[1], "Skip intervening green/red bars and ignore wicks");

flags[2] = false;
Check(!BellUpBoostExit.TryCalculate(bars, flags, 13.37m, out _, out _), "Do not cross an episode boundary");
flags[2] = true;
flags[^1] = false;
Check(!BellUpBoostExit.TryCalculate(bars, flags, 13.37m, out _, out _), "No current confirmed episode means fallback");
Check(!BellUpBoostExit.TryCalculate(bars, [], 13.37m, out _, out _), "Reject unaligned flags");
Check(!BellUpBoostExit.TryCalculate(bars, Enumerable.Repeat(true, bars.Count).ToArray(), 0m, out _, out _), "Reject invalid entry");

var three = Bodies(0.01m, 0.04m, 0.01m, 0.08m, 0.01m, 0.12m);
Check(BellUpBoostExit.TryCalculate(three, Enumerable.Repeat(true, three.Count).ToArray(), 10m, out result, out _)
      && result.AverageBody == 0.10m, "Only the nearest two boosts count");
Check(BellUpEntryTiming.IsBoost(Bodies(0.08m)[0], Bodies(-0.04m)[0]), "Inclusive two-times boundary");
Check(!BellUpEntryTiming.IsBoost(Bodies(-0.08m)[0], Bodies(0.01m)[0]), "Red candles never boost");
Check(BellUpEntryTiming.IsBoost(Bodies(0.01m)[0], Bodies(0m)[0]), "Existing zero-body predecessor semantics retained");

var vet = Bodies(-0.04m, 0.055m, 0.085m, 0.1608m);
Check(!BellUpBoostExit.TryCalculate(vet, [true, true, true, true], 13.37m, out _, out _),
      "VET cached green bodies are not two boosts under the literal 2x rule");
var tiny = Bodies(0m, 0.001m, 0m, 0.001m);
Check(!BellUpBoostExit.TryCalculate(tiny, [true, true, true, true], 10m, out _, out _), "Fallback if rounded target equals entry");

var scanTime = bars[4].Time.AddHours(2);
var completed = BellUpEntryTiming.GetCompletedCandles(bars.AsEnumerable().Reverse(), Timeframe.H4, scanTime);
Check(completed.Count == 4 && completed[^1] == bars[3], "Exclude forming/future H4 bars and sort");
var daily = new[] { new Candle { Time = scanTime.Date.AddDays(-1) }, new Candle { Time = scanTime.Date } };
Check(BellUpEntryTiming.GetCompletedCandles(daily, Timeframe.D1, scanTime.Date.AddHours(15)).Count == 1, "Exclude unfinished Daily bar");
Check(BellUpEntryTiming.GetCompletedCandles(daily, Timeframe.D1, scanTime.Date.AddHours(16)).Count == 2, "Include Daily bar at close");
Console.WriteLine($"Passed {assertions} boost-exit checks.");
