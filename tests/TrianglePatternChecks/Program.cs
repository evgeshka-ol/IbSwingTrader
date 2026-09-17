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

Console.WriteLine($"Passed {assertions} triangle pattern checks.");
