using System.Text.Json.Serialization;

namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateFileDocument
    {
        public CandidateSummarySections Summary { get; set; } = new();

        [JsonPropertyName("ReversalCandidatesData")]
        public List<CandidateDetails> Candidates { get; set; } = [];

        [JsonPropertyName("TodayResearchLikeCandidatesData")]
        public List<CandidateDetails> SameDayCandidates { get; set; } = [];

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("Candidates")]
        public List<CandidateDetails>? LegacyCandidates
        {
            get => null;
            set
            {
                if (value is { Count: > 0 })
                    Candidates = value;
            }
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("SameDayCandidates")]
        public List<CandidateDetails>? LegacySameDayCandidates
        {
            get => null;
            set
            {
                if (value is { Count: > 0 })
                    SameDayCandidates = value;
            }
        }
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
