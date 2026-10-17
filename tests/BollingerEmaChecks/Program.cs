using IbSwingTrader.Application.Dataset;

var checks = 0;
void Equal(decimal actual, decimal expected, string name)
{
    checks++;
    if (Math.Abs(actual - expected) > 0.00000000000000000001m)
        throw new InvalidOperationException($"{name}: {actual} != {expected}");
}
void Check(bool condition, string name)
{
    checks++;
    if (!condition) throw new InvalidOperationException(name);
}
var engine = new FeatureEngine();
var start = new DateTime(2026, 1, 1);
List<Candle> Make(int stepDays) => Enumerable.Range(1, 21).Select((value, i) => new Candle
{
    Time = stepDays == 0 ? start.AddHours(4 * i) : start.AddDays(stepDays * i),
    Open = value == 21 ? 40m : value,
    Close = value == 21 ? 40m : value,
    High = value == 21 ? 40m : value,
    Low = value == 21 ? 40m : value
}).ToList();
// First 20 closes 1..20 seed EMA at 10.5. At close 40, EMA=(2*40+19*10.5)/21.
var expectedMid = (2m * 40m + 19m * 10.5m) / 21m;
// Last 20 closes are 2..20,40: mean=12.45, population variance=68.4475.
var sigma = (decimal)Math.Sqrt(68.4475);
foreach (var step in new[] { 0, 1, 7 })
{
    var bars = Make(step);
    var f = engine.CalculateLast(bars);
    Equal(f.H4BollingerMidBand, expectedMid, $"H4 EMA center/{step}");
    Equal(f.H4BollingerUpperBand, expectedMid + 2m * sigma, $"H4 upper/{step}");
    Equal(f.H4BollingerLowerBand, expectedMid - 2m * sigma, $"H4 lower/{step}");
    Equal(f.H4BollingerBandWidthPct,
        (f.H4BollingerUpperBand - f.H4BollingerLowerBand) / expectedMid * 100m, "Width uses EMA denominator");
    Equal(f.H4BollingerMidBand * (1m + f.H4BollingerMidDistancePct / 100m), 40m,
        "ScanPrice reconstruction uses consistent mid and distance");
    if (step > 0)
    {
        Equal(f.DailyBollingerMidBand, expectedMid, "Native Daily EMA");
        Equal(f.DailyBollingerMidBand * (1m + f.DailyBollingerMidDistancePct / 100m), 40m, "Daily reconstruction");
        Equal(f.DailyMaSignedDistancePct, (40m - 12.45m) / 12.45m * 100m,
            "Legacy plain-MA field remains SMA");
    }
    if (step == 7)
    {
        Equal(f.WeeklyBollingerMidBand!.Value, expectedMid, "Weekly EMA");
        Equal(f.WeeklyBollingerMidBand.Value * (1m + f.WeeklyBollingerMidDistancePct!.Value / 100m),
            40m, "Weekly reconstruction");
    }
    Equal(engine.Calculate(bars, 19).H4BollingerMidBand, 0m, "Need 20 points before Bollinger");
    Equal(engine.Calculate(bars, 20).H4BollingerMidBand, 10.5m, "EMA seed is initial SMA20");
    var future = bars.Concat([new Candle { Time = bars[^1].Time.AddDays(7), Close = 1000m }]).ToList();
    Equal(engine.Calculate(future, 21).H4BollingerMidBand, f.H4BollingerMidBand, "Future bar does not alter prefix");
    var bulk = FeatureEngine.CalculateNativeReversalHistory(bars);
    Equal(bulk[^1].H4BollingerMidBand, f.H4BollingerMidBand, "Watch migration uses same EMA history");
}
var extended = Enumerable.Range(1, 50).Select(i => new Candle
{
    Time = start.AddDays(i), Close = i % 7 + i * 0.1m
}).ToList();
var full = engine.CalculateLast(extended);
var truncated = engine.CalculateLast(extended.TakeLast(20).ToList());
Check(full.H4BollingerMidBand != truncated.H4BollingerMidBand, "EMA must retain earlier history, not reset every window");
Equal(FeatureEngine.CalculateNativeReversalHistory(extended)[^1].H4BollingerMidBand,
    full.H4BollingerMidBand, "Bulk EMA retains prefix beyond 20 points");
Console.WriteLine($"Passed {checks} Bollinger EMA checks.");
