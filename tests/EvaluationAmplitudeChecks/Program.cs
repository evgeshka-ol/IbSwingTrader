using IbSwingTrader.Application.Dataset;

var first = new DateTime(2026, 10, 20, 8, 35, 0);
var second = first.AddHours(2);
var checks = 0;
void Check(string name, decimal expected, decimal scan, decimal entry, DateTime? entered,
    decimal? high, decimal? highPct, DateTime? highTime,
    decimal? low, decimal? lowPct, DateTime? lowTime)
{
    checks++;
    var actual = EvaluationAmplitude.Calculate(scan, entry, entered,
        high, highPct, highTime, low, lowPct, lowTime);
    if (actual != expected)
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
}

Check("Entered uses entry instead of low or scan", 20m, 110m, 100m, first,
    120m, null, second, 80m, null, first);
Check("Entered max-first does not invert entry-relative growth", 20m, 110m, 100m, first,
    120m, null, first, 80m, null, second);
Check("Entered maximum below entry remains negative", -5m, 110m, 100m, first,
    95m, null, second, 80m, null, first);
Check("Nonzero plan without EntryTime is not participation", 50m, 100m, 90m, null,
    120m, null, second, 80m, null, first);
Check("Unfilled playable min-first uses low", 50m, 100m, 0m, null,
    120m, null, second, 80m, null, first);
Check("Unfilled playable max-first retains negative order marker", -20m, 100m, 0m, null,
    120m, null, first, 80m, null, second);
Check("Legacy ungrouped min-first retains diagnostic range", 37.22m, 22.26m, 0m, null,
    24.70m, 10.96m, second, 18m, -19.14m, first);
Check("SMCI minimum above scan is not a pullback below scan", 19.44m, 31.53m, 0m, null,
    37.66m, null, second, 31.54m, null, first);
Check("Same M5 cannot prove min-first", 20m, 100m, 0m, null,
    120m, null, first, 80m, null, first);
Check("Missing minimum time uses scan", 20m, 100m, 0m, null,
    120m, null, second, 80m, null, null);
Check("Missing maximum time uses scan", 20m, 100m, 0m, null,
    120m, null, null, 80m, null, first);
Check("Historical percentages reconstruct missing prices", 50m, 100m, 0m, null,
    null, 20m, second, null, -20m, first);
Check("Historical percentage maximum with entry reference", 20m, 110m, 100m, first,
    null, 120m / 110m * 100m - 100m, second, null, -20m, first);
Check("Exact prices take priority over rounded percentages", 37.22m, 22.26m, 0m, null,
    24.70m, 11m, second, 18m, -19m, first);
Check("Missing low cannot manufacture min-first range", 20m, 100m, 0m, null,
    120m, null, second, null, null, first);
Check("Missing maximum yields zero", 0m, 100m, 0m, null,
    null, null, second, 80m, null, first);
Check("Invalid entered reference yields zero", 0m, 100m, 0m, first,
    120m, null, second, 80m, null, first);
Check("Zero scan still permits valid entry-relative calculation", 20m, 0m, 100m, first,
    120m, null, second, 80m, null, first);
Check("Invalid scan cannot support max-first calculation", 0m, 0m, 0m, null,
    120m, null, first, 80m, null, second);
Check("Zero range", 0m, 100m, 100m, first,
    100m, null, second, 100m, null, first);
void Other(string name, decimal expected, decimal scan, decimal? high, decimal? highPct,
    DateTime? highAt, DateTime? lowAt)
{
    checks++;
    var actual = EvaluationAmplitude.Calculate(scan, 0m, null, high, highPct, highAt,
        8m, -20m, lowAt, "Other");
    if (actual != expected) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
}
Other("Other min-first uses scan", 20m, 10m, 12m, null, second, first);
Other("Other max-first has no minimum-based sign marker", 20m, 10m, 12m, null, first, second);
Other("Other maximum below scan retains actual negative move", -5m, 10m, 9.5m, null, second, first);
Other("Other equal extrema times", 20m, 10m, 12m, null, first, first);
Other("Other missing extrema times", 20m, 10m, 12m, null, null, null);
Other("Other historical percentage fallback", 20m, 10m, null, 20m, second, first);
Other("Other RCUS is scan-relative", 10.96m, 22.26m, 24.70m, null, second, first);
Other("Other NN does not count buying the later minimum", 27.82m, 10.1m, 12.91m, null, second, first);
Other("Other invalid scan", 0m, 0m, 12m, null, second, first);
Other("Other missing maximum", 0m, 10m, null, null, second, first);
Console.WriteLine($"Passed {checks} evaluation amplitude checks.");
