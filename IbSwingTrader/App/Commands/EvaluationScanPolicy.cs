namespace IbSwingTrader.App.Commands
{
    public static class EvaluationScanPolicy
    {
        // All timestamps use the application's exchange-local time convention.
        public static bool IsEligible(DateTime scanTime, DateTime marketNow, DateTime availableUntil)
        {
            return scanTime.Date < marketNow.Date && scanTime < availableUntil;
        }
    }
}
