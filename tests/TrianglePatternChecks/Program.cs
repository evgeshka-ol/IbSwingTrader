using IbSwingTrader.Application.Candidates;

var assertions = 0;
void Check(bool value, string message)
{
    assertions++;
    if (!value) throw new InvalidOperationException(message);
}

bool Match(decimal[] opens, decimal[] closes) =>
    TrianglePatternClassifier.IsPostSpikeConsolidation(opens, closes);

Check(Match([10m, 10m, 12m, 12m, 12m, 12m],
    [9.9m, 12m, 12m, 12m, 12m, 12m]), "Continuous plateau after a spike");
Check(Match([10m, 10m, 10m, 12m, 12m],
    [9.9m, 9.9m, 12m, 12m, 12m]), "Two plateau candles suffice");
Check(!Match([10m, 10m, 12m, 11m, 11m, 11m],
    [9.9m, 12m, 11m, 11m, 11m, 11m]), "Do not hide a large intervening body");
Check(!Match([10m, 10m, 14m, 12m, 12m, 12m],
    [9.9m, 12m, 14m, 12m, 12m, 12m]), "Do not hide an excursion before the last three bars");
Check(!Match([10m, 10m, 9m, 9m, 9m],
    [9.9m, 12m, 9m, 9m, 9m]), "A plateau far below the spike is not consolidation at its top");
Check(!Match([10m, 10m, 14m, 14m, 14m],
    [9.9m, 12m, 14m, 14m, 14m]), "Reject a plateau too far above the spike");
Check(!Match([10m, 10m, 12m, 12m, 12m],
    [9.9m, 12m, 12m, 12m, 12.1m]), "A current impulse prevents falling back to an old spike");
Check(!Match([10m, 10m, 12m, 12m, 12.1m],
    [9.9m, 12m, 12m, 12.1m, 12.1m]), "One candle after the latest impulse is insufficient");
Check(Match([10m, 10m, 12m, 12m, 13m, 13m],
    [9.9m, 12m, 12m, 13m, 13m, 13m]), "A new spike can start its own continuous plateau");
Check(!Match([10m, 10m, 11.1m, 12.9m, 12m],
    [9.9m, 12m, 11.1m, 12.9m, 12m]), "Whole-plateau spread must remain small");
Check(!Match([10m, 10m, 10m, 10m, 10m],
    [10m, 10m, 10m, 10m, 10m]), "No spike means no triangle");
Check(!Match([10m, 10m, 12m, 12m], [9.9m, 12m, 12m, 12m]), "Keep minimum history requirement");
Check(!Match([10m, 10m, 12m, 12m, 12m], [9.9m, 12m]), "Reject unaligned series");

var multiRampCloses = new[] { 10m, 10.4m, 11m, 11.7m, 12m, 12.05m, 12.1m, 12.08m, 12.12m, 12.1m };
var multiRampOpens = new[] { 9.9m, 10m, 10.4m, 11m, 11.7m, 12.05m, 12.1m, 12.05m, 12.08m, 12.12m };
var widths = new[] { 1m, 1.2m, 1.5m, 2m, 2.4m, 2.2m, 1.9m, 1.6m, 1.3m, 1m };
var upper = multiRampCloses.Select((x, i) => x + widths[i] / 2m).ToArray();
var lower = multiRampCloses.Select((x, i) => x - widths[i] / 2m).ToArray();
var mid = multiRampCloses.ToArray();
Check(Match(multiRampOpens, multiRampCloses, upper, mid, lower),
    "Multi-candle rise followed by a contracting plateau");

var activeExpansionCloses = new[] { 10m, 10.4m, 11m, 11.7m, 12m, 12.05m, 12.1m, 12.8m, 13.5m, 14m };
var activeExpansionOpens = new[] { 9.9m, 10m, 10.4m, 11m, 11.7m, 12.05m, 12.1m, 12.1m, 12.8m, 13.5m };
var activeWidths = new[] { 1m, 1.2m, 1.5m, 2m, 2.4m, 2.5m, 2.7m, 3m, 3.4m, 3.8m };
var activeUpper = activeExpansionCloses.Select((x, i) => x + activeWidths[i] / 2m).ToArray();
var activeLower = activeExpansionCloses.Select((x, i) => x - activeWidths[i] / 2m).ToArray();
Check(!Match(activeExpansionOpens, activeExpansionCloses, activeUpper, activeExpansionCloses, activeLower),
    "Active late expansion remains BellUp-like");

Console.WriteLine($"Passed {assertions} triangle pattern checks.");
