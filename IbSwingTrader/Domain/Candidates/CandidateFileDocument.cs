namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateFileDocument
    {
        public CandidateSummarySections Summary { get; set; } = new();

        public List<CandidateDetails> Candidates { get; set; } = [];

        public List<CandidateDetails> SameDayCandidates { get; set; } = [];
    }

    public class CandidateSummarySections
    {
        public List<CandidateSummaryItem> ReversalCandidates { get; set; } = [];

        public List<CandidateSummaryItem> TodayResearchLikeCandidates { get; set; } = [];
    }

    public class CandidateSummaryItem
    {
        public required string Ticker { get; set; }
    }
}
