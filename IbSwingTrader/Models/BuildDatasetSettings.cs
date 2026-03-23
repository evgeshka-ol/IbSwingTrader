namespace IbSwingTrader.Models
{
    public class BuildDatasetSettings
    {
        public int MinimumCandlesRequired { get; set; } = 60;
        public bool IncludeSyntheticTrades { get; set; } = false;
        public int MaxParallelTickers { get; set; } = 3;
        public int HistoryWarmupDays { get; set; } = 120;
        public int FuturePaddingDays { get; set; } = 21;
        public List<int> EntryShifts { get; set; } = [-12, -9, -6, -3, 0];

        public Dictionary<string, string> TickerAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}