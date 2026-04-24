namespace IbSwingTrader.Domain.Settings
{
    public class BuildEvaluationDatasetSettings
    {
        public DateTime? MinScanTime { get; set; }

        public decimal? MinAmplitudePct { get; set; }

        public List<BuildEvaluationDatasetSortColumnSettings> SortColumns { get; set; } =
        [
            new() { Column = "GroupLabel", OrderedValues = ["TradeCandidate", "Wishlist", "FilterReference"] },
            new() { Column = "ScanTime", Descending = true },
            new() { Column = "Outcome", OrderedValues = ["Win", "NoEntry", "Loss", "Open", "InsufficientFutureData"] },
            new() { Column = "ExtremumOrder", OrderedValues = ["MinFirst", "MaxFirst", "SameBar"] },
            new() { Column = "ExtremumSubgroup" },
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
