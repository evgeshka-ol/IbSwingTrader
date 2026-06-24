namespace IbSwingTrader.Domain.Settings
{
    public class ResearchSettings
    {
        public string Mode { get; set; } = "top_gainers";
        public string Source { get; set; } = "tws_top_gainers";
        public string OutputFile { get; set; } = "datasets/research_top_gainers.csv";
        public int? RecentScanDays { get; set; } = 0;
        public int? RecentEvaluationScanDays { get; set; } = 7;
        public List<string> ScanCodes { get; set; } =
        [
            "TOP_PERC_GAIN",
            "TOP_OPEN_PERC_GAIN"
        ];
        public int LookbackCalendarDays { get; set; } = 240;
        public int MinimumCandles { get; set; } = 120;
        public int MaxParallelTickers { get; set; } = 3;
        public int ContractResolveTimeoutSeconds { get; set; } = 60;
        public int LocalExtremaLookbackBars { get; set; } = 3;
        public int EpisodeMergeCooldownBars { get; set; } = 8;
        public int MaxBarsToPeak { get; set; } = 30;
        public decimal MinRunupPct { get; set; } = 10m;
        public List<ResearchSortColumnSettings> SortColumns { get; set; } =
        [
            new() { Column = "ScanTime", Descending = true },
            new() { Column = "AmplitudePct", Descending = true },
            new() { Column = "Ticker" }
        ];
    }

    public class ResearchSortColumnSettings
    {
        public string Column { get; set; } = string.Empty;

        public bool Descending { get; set; }

        public List<string> OrderedValues { get; set; } = [];
    }
}
