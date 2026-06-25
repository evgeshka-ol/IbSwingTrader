namespace IbSwingTrader.Domain.Settings
{
    public class PathSettings
    {
        public string DataRoot { get; set; } = "Data";
        public string TradesFile { get; set; } = "trades.csv";
        public string DatasetFile { get; set; } = "datasets/trade_dataset.csv";
        public string CacheFolder { get; set; } = "cache";
        public string CandidatesFile { get; set; } = "Tickers/candidates.csv";
        public string EvaluationsFile { get; set; } = "Tickers/evaluations.csv";
        public string EvaluationsArchiveFile { get; set; } = "Tickers/evaluations_archive.csv";
        public string WishListFile { get; set; } = "Tickers/wishlist.csv";
        public string ProcessedCandidateFilesManifest { get; set; } =
            "manifests/processed-candidate-files.json";
        public string LogsFolder { get; set; } = "logs";
    }
}
