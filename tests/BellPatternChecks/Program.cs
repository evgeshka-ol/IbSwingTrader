using IbSwingTrader.Application.Candidates;

var assertions = 0;
void Check(bool value, string message)
{
    assertions++;
    if (!value) throw new InvalidOperationException(message);
}

// The local four-bar geometry is a clear opening launch, while the full
// mid-band history still slopes down. This is a diagnostic transition, not a
// normal Daily BellUp admission.
var upper = new[] { 11m, 10m, 9m, 9m, 9.5m, 10.5m, 12m };
var mid = new[] { 10m, 9m, 8m, 8m, 8.2m, 8.5m, 8.8m };
var lower = new[] { 9m, 8m, 7m, 7m, 6.8m, 6.4m, 5.8m };

Check(
    BellPatternClassifier.ClassifyBellPatternKindForTimeframe(
        upper, mid, lower, BollingerFigureDirection.Down, BellPatternTimeframe.Daily) == BellPatternKind.None,
    "A down-directed Daily mid must remain outside normal BellUp admission");
Check(
    BellPatternClassifier.IsDailyTransitionBellUp(upper, mid, lower, BollingerFigureDirection.Down),
    "Strong Daily BellUp geometry under a down-directed mid must be observable as a transition");
Check(
    !BellPatternClassifier.IsDailyTransitionBellUp(upper, mid, lower, BollingerFigureDirection.Up),
    "A non-descending Daily mid is not a transition state");
Check(
    BellPatternClassifier.ClassifyBellPatternKindForTimeframe(
        upper, mid, lower, BollingerFigureDirection.Up, BellPatternTimeframe.Daily) == BellPatternKind.BellUp,
    "The same geometry remains a normal BellUp once the Daily mid direction has turned up");

var reversalClose = new[] { 10m, 9m, 8m, 7m, 6.5m, 6.3m };
var reversalUpper = new List<decimal> { 11m, 10.5m, 10m, 9.5m, 9.1m, 8.8m };
var reversalMid = new List<decimal> { 10m, 9.5m, 9m, 8.6m, 8.35m, 8.2m };
var reversalLower = new List<decimal> { 9m, 8m, 7m, 6.2m, 5.6m, 5.2m };
var reversalRsi = new List<decimal> { 45m, 40m, 35m, 30m, 31m, 33m };
var reversalHistogram = new List<decimal> { -1m, -1.2m, -1.4m, -1.5m, -1.45m, -1.3m };
Check(
    BellPatternClassifier.IsReversalHookPreparing(
        reversalClose, reversalUpper, reversalMid, reversalLower, reversalRsi, reversalHistogram, out _),
    "A decelerating lower-band decline with improving confirmation is a ReversalHookPreparing diagnostic");

Console.WriteLine($"Passed {assertions} Bell pattern checks.");
