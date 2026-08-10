namespace IbSwingTrader.Domain.Settings
{
    // Single source of truth for the Recent*Series window lengths. Previously duplicated
    // identically as private consts in CandidateFinder.cs, EvaluationDatasetBuilder.cs,
    // TradeDatasetBuilder.cs, BuildResearchDatasetCommand.cs, NormalizeReportsCommand.cs,
    // and HistoricalCache.cs.
    public static class RecentSeriesWindow
    {
        public const int Daily = 30;
        public const int Weekly = 20;
        public const int H4 = 40;

        // Bounds for BollingerSwingBoundaryDetector: the floor matches the old fixed window
        // lengths so a detected boundary can never be tighter than what was already validated;
        // the ceiling keeps the detector from walking back into an unrelated earlier cycle.
        public const int SwingDetectorMinDailyLookback = 4;
        public const int SwingDetectorMaxDailyLookback = 20;
        public const int SwingDetectorMinWeeklyLookback = 4;
        public const int SwingDetectorMaxWeeklyLookback = 14;
        public const int SwingDetectorMinH4Lookback = 4;
        public const int SwingDetectorMaxH4Lookback = 24;
    }
}
