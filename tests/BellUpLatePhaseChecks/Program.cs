using IbSwingTrader.Application.Candidates;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
// SPT October22: Daily was already BellUp on the preceding point. Daily RSI
// 80.04 -> 77.99 used to veto it, despite continuing Daily bands/MACD expansion.
// Saved H4 cooling is retained as input but cannot resurrect that deleted veto.
Check(!BellUpLatePhase.IsLate(true, [79.3m, 78.88m, 75m, 76.44m, 75.11m, 69.8m],
    [.09m, .10m, .11m, .11m, .10m, .08m], 1.10m, out _),
    "SPT-like established Daily expansion is not late from the removed Daily RSI condition");
Check(BellUpLatePhase.IsLate(false, [65m, 76m, 74m], [.1m, .2m, .15m], 1m, out var late) &&
    late.Contains("only after H4 momentum"), "New Daily confirmation after combined H4 momentum decay remains late");
Check(!BellUpLatePhase.IsLate(false, [65m, 76m, 74m], [.1m, .2m, .21m], 1m, out _),
    "H4 RSI rollover alone cannot trigger the combined guard");
Check(!BellUpLatePhase.IsLate(false, [65m, 76m, 77m], [.1m, .2m, .15m], 1m, out _),
    "MACD decay alone cannot trigger the combined guard");
Check(BellUpLatePhase.IsLate(true, [74m, 75m], [.1m, .12m], 4m, out var terminal) &&
    terminal.Contains("terminal expansion"), "Existing terminal band-expansion guard remains inclusive");
Check(!BellUpLatePhase.IsLate(true, [80m, 85m], [.1m, .12m], 3.99m, out _),
    "High rising RSI alone is not a terminal-phase veto");
Check(!BellUpLatePhase.IsLate(true, [80m, 74.99m], [.12m, .1m], 4m, out _),
    "Preserve the existing combined terminal guard's RSI boundary");
Check(!BellUpLatePhase.IsLate(true, [], [], 10m, out _), "Missing H4 evidence does not invent a late-phase diagnosis");
Console.WriteLine($"Passed {checks} BellUp late phase checks.");
