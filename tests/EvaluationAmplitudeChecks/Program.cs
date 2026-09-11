using IbSwingTrader.Application.Dataset;

var max = new DateTime(2026, 9, 10, 6, 50, 0);
var min = new DateTime(2026, 9, 11, 4, 5, 0);
var cases = new (string Name, decimal Value, DateTime? Min, DateTime? Max, decimal Expected)[]
{
    ("SMR max before min remains magnitude", 11.68m, min, max, 11.68m),
    ("Min before max", 11.68m, max, min, 11.68m),
    ("Repeated normalization is idempotent", -11.68m, min, max, 11.68m),
    ("Extrema order remains a separate field", -11.68m, max, min, 11.68m),
    ("Same bar has unknown order", 11.68m, min, min, 11.68m),
    ("Missing minimum time", 11.68m, null, max, 11.68m),
    ("Missing maximum time", 11.68m, min, null, 11.68m),
    ("Both timestamps missing", 11.68m, null, null, 11.68m),
    ("Zero range", 0m, min, max, 0m),
    ("Same-day decline", 5m, max.AddHours(1), max, -5m)
};
foreach (var test in cases)
{
    var actual = EvaluationAmplitude.WithDirection(test.Value, test.Min, test.Max);
    if (actual != test.Expected)
        throw new InvalidOperationException($"{test.Name}: expected {test.Expected}, got {actual}");
}
Console.WriteLine($"Passed {cases.Length} signed-amplitude checks.");
