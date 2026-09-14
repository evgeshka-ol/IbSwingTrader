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
Check(result.BoostCount == 2, "Record both selected boosts");

var oneBoost = Bodies(0.01m, 0.04m, 0.03m, 0.05m);
Check(BellUpBoostExit.TryCalculate(oneBoost, Enumerable.Repeat(true, oneBoost.Count).ToArray(), 10m, out result, out _)
      && result.BoostCount == 1 && result.AverageBody == 0.04m && result.ExitPrice == 10.04m
      && result.EarlierBoost == null, "Use one qualifying boost when a second is unavailable");

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
bool Turn(decimal[] lower, out int trough) => BellUpLowerBandTurn.TryFindConfirmedTurn(
    lower, Enumerable.Repeat(true, lower.Length).ToArray(), out trough);
Check(Turn([86.10m, 85.83m, 84.77m, 83.35m, 82.63m, 82.34m, 82.85m, 82.93m, 83.20m, 83.80m], out var trough)
      && trough == 5, "INTC: two completed points above the episode trough");
Check(!Turn([5m, 4m, 3m, 2m], out _), "Continued decline is not a turn");
Check(!Turn([5m, 4m, 3m, 3.2m], out _), "One higher point is insufficient");
Check(Turn([5m, 4m, 3m, 3m, 3.1m, 3.2m], out trough) && trough == 3, "Flat trough uses its final point");
Check(!Turn([2m, 3m, 4m, 5m], out _), "No preceding decline means no veto");
Check(!Turn([5m, 4m, 3m, 3.2m, 2.9m], out _), "A new low cancels the old turn");
Check(!Turn([5m, 4m, 3m, 3.2m, 3m], out _), "Latest point equal to trough is not above it");
Check(Turn([5m, 4m, 3m, 3.5m, 3.2m], out _), "Both points above trough suffice; consecutive rises are not required");
Check(!BellUpLowerBandTurn.TryFindConfirmedTurn([5m, 4m, 3m, 3.1m, 3.2m], [true, true, false, true, true], out _),
      "Do not borrow a trough from an old episode");
Check(!BellUpLowerBandTurn.TryFindConfirmedTurn([5m, 4m, 3m, 3.1m, 3.2m], [true, true, true, true, false], out _),
      "No current confirmation means no phase veto");
Check(!Turn([2m, 1m, 0m, 1m, 2m], out _), "Missing zero-band history is not a turn");
Check(!BellUpLowerBandTurn.TryFindConfirmedTurn([5m, 4m, 3m, 4m], [true], out _), "Reject unaligned turn inputs");
List<Candle> Late(params (decimal Open, decimal High, decimal Low, decimal Close)[] values) => values.Select((v, i) => new Candle
{
    Time = new DateTime(2026, 9, 8).AddHours(i * 4), Open = v.Open, High = v.High, Low = v.Low, Close = v.Close
}).ToList();
var late = Late((10m, 10.2m, 9.9m, 10.1m), (10m, 10.3m, 9.9m, 10.2m),
                (10m, 10.4m, 9.9m, 10.1m), (10m, 11m, 9.9m, 11m),
                (11m, 11.3m, 10.9m, 11.2m), (11.2m, 12m, 11m, 12m));
Check(BellUpLateEntryPenalty.Calculate(late.Select(x => x.Open).ToList(), late.Select(x => x.High).ToList(),
      late.Select(x => x.Low).ToList(), late.Select(x => x.Close).ToList()) == 1.5m, "Two comparable prior boosts penalize late peak");
var one = late.Take(5).Append(new Candle { Open = 11.2m, High = 11.5m, Low = 11.1m, Close = 11.4m }).ToList();
Check(BellUpLateEntryPenalty.Calculate(one.Select(x => x.Open).ToList(), one.Select(x => x.High).ToList(),
      one.Select(x => x.Low).ToList(), one.Select(x => x.Close).ToList()) == 0.75m, "One prior boost uses softer penalty");
var notPeak = one.Select(x => x).ToList(); notPeak[^1].Close = 11.25m;
Check(BellUpLateEntryPenalty.Calculate(notPeak.Select(x => x.Open).ToList(), notPeak.Select(x => x.High).ToList(),
      notPeak.Select(x => x.Low).ToList(), notPeak.Select(x => x.Close).ToList()) == 0m, "No penalty away from candle high");
Console.WriteLine($"Passed {assertions} BellUp exit, lower-band and late-entry checks.");
