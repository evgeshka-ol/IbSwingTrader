namespace IbSwingTrader.Domain.Settings
{
    public class BuildDatasetSettings
    {
        public int MinimumCandlesRequired { get; set; } = 60;
        public bool IncludeSyntheticTrades { get; set; } = false;
        public int MaxParallelTickers { get; set; } = 3;
        public int HistoryWarmupDays { get; set; } = 120;
        public int FuturePaddingDays { get; set; } = 21;
        public List<decimal> SyntheticEntryFractions { get; set; } = [0.25m, 0.5m, 0.75m];

        public Dictionary<string, string> TickerAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
