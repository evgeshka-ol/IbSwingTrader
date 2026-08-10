namespace IbSwingTrader.Domain.Settings
{
    public class BuildDatasetSettings
    {
        public int MinimumCandlesRequired { get; set; } = 60;
        public int MaxParallelTickers { get; set; } = 3;
        public int HistoryWarmupDays { get; set; } = 120;
        public int FuturePaddingDays { get; set; } = 21;

        // Raw broker exports can split one real position across several fills
        // (e.g. a per-order share cap forcing multiple entry tickets). Fills for the
        // same ticker/direction within this window of each other are merged into one trade.
        public int FillMergeWindowMinutes { get; set; } = 30;

        // Merged trades below this entry size are dropped as probe/experiment orders
        // rather than real trading decisions (see TradePositionMerger).
        public int MinimumEntryQuantity { get; set; } = 5;

        public Dictionary<string, string> TickerAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
