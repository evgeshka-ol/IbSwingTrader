using IbSwingTrader.App.Commands;

var now = new DateTime(2026, 9, 11, 4, 50, 3);
var availableUntil = now.AddMinutes(-10);
var checks = new (string Name, DateTime Scan, DateTime Now, DateTime Until, bool Expected)[]
{
    ("Yesterday remains eligible after a new scan", new(2026, 9, 10, 6, 42, 27), now, availableUntil, true),
    ("Actual September 11 fresh scan is deferred", new(2026, 9, 11, 4, 39, 16), now, availableUntil, false),
    ("Current-day scan stays deferred in the evening", new(2026, 9, 11, 4, 39, 16), now.Date.AddHours(20), now.Date.AddHours(19), false),
    ("Deferred scan becomes eligible next day", new(2026, 9, 11, 4, 39, 16), now.AddDays(1), availableUntil.AddDays(1), true),
    ("Old retry remains eligible", new(2026, 9, 8, 7, 51, 20), now, availableUntil, true),
    ("Future scan is deferred", now.AddDays(1), now, availableUntil, false),
    ("Safety lag is enforced across midnight", new(2026, 9, 10, 23, 59, 0), now.Date.AddMinutes(5), now.Date.AddMinutes(-5), false),
    ("Available cutoff is exclusive", availableUntil.AddDays(-1), now, availableUntil.AddDays(-1), false)
};

foreach (var check in checks)
{
    var actual = EvaluationScanPolicy.IsEligible(check.Scan, check.Now, check.Until);
    if (actual != check.Expected)
        throw new InvalidOperationException($"{check.Name}: expected {check.Expected}, got {actual}");
}
Console.WriteLine($"Passed {checks.Length} evaluation eligibility checks.");
