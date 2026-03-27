namespace IbSwingTrader.Domain.WishList
{
    public class ProcessedCandidateFile
    {
        public required string FileName { get; set; }
        public required string FullPath { get; set; }

        public long FileSize { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public required string Sha256 { get; set; }

        public DateTime ProcessedAtUtc { get; set; }

        public int CandidateCount { get; set; }
        public int EvaluationCount { get; set; }

        public string Status { get; set; } = "Completed";

        public required string OutputCsvFileName { get; set; }
        public required string OutputCsvFullPath { get; set; }
    }
}
