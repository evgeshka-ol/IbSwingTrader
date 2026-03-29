namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateFileDocument
    {
        public List<CandidateSummaryItem> Summary { get; set; } = [];

        public List<CandidateDetails> Candidates { get; set; } = [];
    }

    public class CandidateSummaryItem
    {
        public required string Ticker { get; set; }
    }
}
