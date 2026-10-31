namespace IbSwingTrader.App.Commands
{
    public static class EvaluationScanPolicy
    {
        // All timestamps use the application's exchange-local time convention.
        public static bool IsEligible(DateTime scanTime, DateTime marketNow, DateTime availableUntil)
        {
            return scanTime.Date < marketNow.Date && scanTime < availableUntil;
        }

        public static bool IsWaitingForEntry(string outcome, string group, bool hasValidPlan,
            bool entered, DateTime scanTime, DateTime marketToday, int forwardDays) =>
            !entered && hasValidPlan && group is ("BellUp" or "ReversalHook" or "Runaway" or "Reversal") &&
            outcome is ("NoEntry" or "IncompleteHistory") &&
            (marketToday.Date - scanTime.Date).Days >= 0 &&
            (marketToday.Date - scanTime.Date).Days < Math.Max(1, forwardDays);
    }
}
