namespace IbSwingTrader.Application.Candidates
{
    public static class BellUpLatePhase
    {
        // Called only for a confirmed Daily BellUp. Daily RSI is deliberately
        // not an input: an isolated overbought rollover cannot establish exhaustion.
        public static bool IsLate(bool previousDailyBellUp, IReadOnlyList<decimal> h4Rsi,
            IReadOnlyList<decimal> h4Histogram, decimal terminalOpeningPct, out string reason)
        {
            reason = string.Empty;
            if (h4Rsi.Count < 2 || h4Histogram.Count < 2)
                return false;
            if (!previousDailyBellUp && h4Rsi.TakeLast(4).Max() >= 70m &&
                h4Rsi[^1] < h4Rsi[^2] && h4Histogram[^1] < h4Histogram[^2])
            {
                reason = "Daily BellUp appeared only after H4 momentum had already rolled over";
                return true;
            }
            if (previousDailyBellUp && h4Rsi[^1] >= 75m && terminalOpeningPct >= 4m)
            {
                reason = "Daily BellUp is no longer new and H4 bands are already in terminal expansion";
                return true;
            }
            return false;
        }
    }
}
