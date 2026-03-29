namespace IbSwingTrader.Domain.Settings
{
    public class PathSettings
    {
        public string DataRoot { get; set; } = "Data";
        public string TradesFile { get; set; } = "trades.csv";
        public string DatasetFile { get; set; } = "dataset.csv";
        public string CacheFolder { get; set; } = "cache";
        public string CandidatesFile { get; set; } = "Tickers/candidates.json";
        public string EvaluationsFile { get; set; } = "Tickers/evaluations.csv";
        public string WishListFile { get; set; } = "Tickers/wishlist.json";
        public string ProcessedCandidateFilesManifest { get; set; } =
            "manifests/processed-candidate-files.json";
        public string LogsFolder { get; set; } = "logs";
    }
}
