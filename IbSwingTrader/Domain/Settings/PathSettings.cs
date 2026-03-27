namespace IbSwingTrader.Domain.Settings
{
    public class PathSettings
    {
        public string DataRoot { get; set; } = "Data";
        public string TradesFile { get; set; } = "trades.csv";
        public string DatasetFile { get; set; } = "dataset.csv";
        public string CacheFolder { get; set; } = "cache";
        public string CandidatesFolder { get; set; } = "candidates";
        public string EvaluationsFolder { get; set; } = "evaluations";
        public string WishListFile { get; set; } = "wishlist/current.json";
        public string ProcessedCandidateFilesManifest { get; set; } =
            "manifests/processed-candidate-files.json";
        public string LogsFolder { get; set; } = "logs";
    }
}