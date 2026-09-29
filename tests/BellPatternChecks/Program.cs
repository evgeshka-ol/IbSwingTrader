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

var reversalClose = new[] { 10m, 9m, 8m, 7m, 6.5m, 6.8m };
var reversalUpper = new List<decimal> { 11m, 10.5m, 10m, 9.5m, 9.1m, 8.8m };
var reversalMid = new List<decimal> { 10m, 9.5m, 9m, 8.6m, 8.35m, 8.2m };
var reversalLower = new List<decimal> { 9m, 8m, 7m, 6.2m, 5.6m, 5.2m };
var reversalMacdLine = new List<decimal> { -2m, -2.5m, -3m, -3.5m, -3.6m, -3.5m };
var reversalMacdSignal = new List<decimal> { -1.5m, -2.2m, -2.9m, -3.6m, -3.9m, -4m };
var reversalHistogram = new List<decimal> { -1m, -1.2m, -1.4m, -1.5m, -1.45m, -1.3m };
Check(
    BellPatternClassifier.IsReversalHookPreparing(
        reversalClose, reversalUpper, reversalMid, reversalLower,
        reversalMacdLine, reversalMacdSignal, reversalHistogram, out _),
    "A price turn with a confirmed MACD turn is a ReversalHookPreparing diagnostic");

var macdLedClose = new[] { 95m, 90m, 85m, 80m, 76m, 86m };
var macdLedUpper = new List<decimal> { 115m, 113m, 111m, 109m, 108m, 108.5m };
var macdLedMid = new List<decimal> { 110m, 108m, 106m, 104m, 103.5m, 102.5m };
var macdLedLower = new List<decimal> { 90m, 85m, 80m, 76m, 73m, 71.5m };
var macdLedLine = new List<decimal> { -3m, -3.5m, -4m, -4.5m, -4.4m, -4.1m };
var macdLedSignal = new List<decimal> { -2m, -2.8m, -3.6m, -4.4m, -4.7m, -4.8m };
var macdLedHistogram = new List<decimal> { -1m, -1.5m, -2m, -2.5m, -2m, -1.3m };
Check(
    BellPatternClassifier.IsReversalHookPreparing(
        macdLedClose, macdLedUpper, macdLedMid, macdLedLower,
        macdLedLine, macdLedSignal, macdLedHistogram, out _),
    "An AEHR-like MACD-led price turn remains diagnostic even while the lower band falls");

Console.WriteLine($"Passed {assertions} Bell pattern checks.");
