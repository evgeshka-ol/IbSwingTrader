global using IbSwingTrader.Domain.Market;
global using IbSwingTrader.Domain.Dataset;
using IbSwingTrader.Application.WishList;
using IbSwingTrader.Application.Dataset;
using IbSwingTrader.Application.Candidates;
using System.Text.Json;

var assertions = 0;
void Check(bool value, string message)
{
    assertions++;
    if (!value) throw new InvalidOperationException(message);
}
var t = new DateTime(2026, 8, 18, 16, 0, 0);
Check(!ReversalWatchState.IsDeepBelowMid(99.99m, 100m, 5m), "A slight mid crossing must not qualify");
Check(!ReversalWatchState.IsDeepBelowMid(95.01m, 100m, 5m), "4.99% must not qualify");
Check(ReversalWatchState.IsDeepBelowMid(95m, 100m, 5m), "Exactly 5% must qualify");
Check(ReversalWatchState.IsDeepBelowMid(90m, 100m, 5m), "Deeper drops must qualify");
Check(!ReversalWatchState.IsDeepBelowMid(7.35m, 7.3635m, 5m), "WEAV's saved shallow crossing must not qualify");
Check(!ReversalWatchState.IsDeepBelowMid(0m, 100m, 5m), "Unknown price cannot qualify");
Check(!ReversalWatchState.IsDeepBelowMid(90m, 0m, 5m), "Unknown mid cannot qualify");
Check(!ReversalWatchState.IsDeepBelowMid(90m, 100m, 0m), "Invalid zero threshold must not weaken admission");

var state = new ReversalWatchState();
state.StartCycle(t.AddDays(-5));
Check(state.ObserveHook(t), "First recognized hook must be recorded");
Check(state.AwaitingNewDrop && !state.CanStartNewCycle, "A hook consumes its watch cycle even without a playable plan");
Check(!state.ObserveHook(t), "The same seed on another prefix must not restart the state");
state.ObservePosition(t.AddDays(1), true);
Check(!state.CanStartNewCycle, "Repeated loser sightings in the same deep zone must not re-enroll");
state.ObservePosition(t.AddDays(-1), false);
Check(!state.CanStartNewCycle, "Pre-hook recovery cannot arm a post-hook drop");
state.ObservePosition(t.AddDays(2), false);
Check(state.CanStartNewCycle, "Recovery out of the deep zone arms a new significant excursion");
var restored = JsonSerializer.Deserialize<ReversalWatchState>(JsonSerializer.Serialize(state))!;
Check(restored.AwaitingNewDrop && restored.CanStartNewCycle, "Retirement/rearm survive restart outside active wishlist");
restored.StartCycle(t.AddDays(3));
Check(!restored.AwaitingNewDrop, "A qualified new drop starts another active cycle");
Check(!restored.ObserveHook(t), "Old hook must not retire the new drop cycle");
Check(restored.ObserveHook(t.AddDays(4)) && restored.AwaitingNewDrop, "A new hook consumes the new cycle");

var migrated = new ReversalWatchState
{
    BollingerModel = "legacy-SMA", LastDailyBar = t.Date, LastH4Bar = t,
    LastHookAt = t, AwaitingNewDrop = true, LeftDropZoneSinceHook = true
};
migrated.EnsureIndicatorModel(FeatureEngine.BollingerModel);
Check(migrated.LastDailyBar == null && migrated.LastH4Bar == null,
    "Indicator-model migration replays local history instead of reusing SMA cursors");
Check(migrated.AwaitingNewDrop && migrated.LastHookAt == t && !migrated.LeftDropZoneSinceHook,
    "Model migration retains consumed hook memory and requires new-model rearm");

// Bulk migration indicators must equal the existing per-prefix feature engine.
var engine = new FeatureEngine();
foreach (var frame in new[] { Timeframe.D1, Timeframe.H4 })
{
    var bars = Enumerable.Range(0, 85).Select(i => new Candle
    {
        Timeframe = frame,
        Time = frame == Timeframe.D1 ? t.Date.AddDays(i) : t.Date.AddHours(4 * i),
        Open = 20m + i % 9, Close = 20m + (i % 13) * 0.21m - i * 0.015m,
        High = 25m, Low = 15m, Volume = 1000m
    }).ToList();
    var bulk = FeatureEngine.CalculateNativeReversalHistory(bars);
    for (var i = 0; i < bars.Count; i++)
    {
        var reference = engine.Calculate(bars, i + 1);
        var f = bulk[i];
        Check(frame == Timeframe.D1
            ? (f.DailyBollingerUpperBand, f.DailyBollingerMidBand, f.DailyBollingerLowerBand,
               f.DailyRSI14, f.DailyMACDLine, f.DailyMACDSignal, f.DailyMACDHistogram) ==
              (reference.DailyBollingerUpperBand, reference.DailyBollingerMidBand, reference.DailyBollingerLowerBand,
               reference.DailyRSI14, reference.DailyMACDLine, reference.DailyMACDSignal, reference.DailyMACDHistogram)
            : (f.H4BollingerUpperBand, f.H4BollingerMidBand, f.H4BollingerLowerBand,
               f.RSI14, f.MACDLine, f.MACDSignal, f.MACDHistogram) ==
              (reference.H4BollingerUpperBand, reference.H4BollingerMidBand, reference.H4BollingerLowerBand,
               reference.RSI14, reference.MACDLine, reference.MACDSignal, reference.MACDHistogram),
            $"Bulk indicators must preserve prefix arithmetic: {frame}/{i}");
    }
}

// A seed is a particular completed bar, not the later diagnosis timestamp.
List<decimal> closes = [8m, 7.7m, 7.4m, 7m, 7.2m, 6.7m, 7m];
List<decimal> mid = [11m, 10m, 9m, 8m, 7.9m, 7.8m, 7.7m];
List<decimal> histogram = [-1m, -1m, -1m, -1m, -0.5m, -0.4m, -0.3m];
var found = BellPatternClassifier.TryFindReversalHookSignal(closes,
    mid.Select(x => x + 2m).ToList(), mid, mid.Select(x => x - 2m).ToList(),
    [], [], [], histogram, out var age, out _);
Check(found && age == 2, "Recognition must expose the actual historical seed age");
var forming = new Candle { Time = t.Date.AddHours(12), Close = 7m };
Check(BellUpEntryTiming.GetCompletedCandles([forming], Timeframe.H4, t.AddMinutes(-1)).Count == 0,
    "A forming H4 cannot retire a watch cycle");
Console.WriteLine($"Passed {assertions} reversal watch checks.");
