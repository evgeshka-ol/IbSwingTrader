namespace IbSwingTrader.Domain.Settings
{
    public class BuildEvaluationDatasetSettings
    {
        public DateTime? MinScanTime { get; set; }

        public int? RecentScanDays { get; set; }

        public int? BackfillLegacyNoEntryZeroAmplitudeDays { get; set; }

        public decimal? MinAmplitudePct { get; set; }

        public bool SkipSeriesRebuildWhenPresent { get; set; } = true;

        public bool SkipCacheMetricsRebuildWhenPresent { get; set; } = true;

        public List<BuildEvaluationDatasetSortColumnSettings> SortColumns { get; set; } =
        [
            new() { Column = "ScanTime", Descending = true },
            new() { Column = "CandidateGroup", OrderedValues = ["Runaway", "Reversal"] },
            new() { Column = "CandidateDisplayRank" },
            new() { Column = "Ticker" }
        ];
    }

    public class BuildEvaluationDatasetSortColumnSettings
    {
        public string Column { get; set; } = string.Empty;

        public bool Descending { get; set; }

        public List<string> OrderedValues { get; set; } = [];
    }
}
