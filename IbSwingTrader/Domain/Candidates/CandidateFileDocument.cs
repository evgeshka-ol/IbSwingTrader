using System.Text.Json.Serialization;

namespace IbSwingTrader.Domain.Candidates
{
    public class CandidateFileDocument
    {
        public CandidateSummarySections Summary { get; set; } = new();

        [JsonPropertyName("ReversalData")]
        public List<CandidateDetails> Candidates { get; set; } = [];

        [JsonPropertyName("RunawayData")]
        public List<CandidateDetails> SameDayCandidates { get; set; } = [];

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("ReversalCandidatesData")]
        public List<CandidateDetails>? LegacyReversalCandidates
        {
            get => null;
            set
            {
                if (value is { Count: > 0 })
                    Candidates = value;
            }
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("TodayResearchLikeCandidatesData")]
        public List<CandidateDetails>? LegacyTodayResearchLikeCandidates
        {
            get => null;
            set
            {
                if (value is { Count: > 0 })
                    SameDayCandidates = value;
            }
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("RunawayCandidatesData")]
        public List<CandidateDetails>? LegacyRunawayCandidates
        {
            get => null;
            set
            {
                if (value is { Count: > 0 })
                    SameDayCandidates = value;
            }
        }

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
        public List<CandidateSummaryItem> Runaway { get; set; } = [];

        public List<CandidateSummaryItem> Reversal { get; set; } = [];

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<CandidateSummaryItem>? RunawayCandidates
        {
            get => null;
            set
            {
                if (value is { Count: > 0 })
                    Runaway = value;
            }
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<CandidateSummaryItem>? TodayResearchLikeCandidates
        {
            get => null;
            set
            {
                if (value is { Count: > 0 })
                    Runaway = value;
            }
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<CandidateSummaryItem>? ReversalCandidates
        {
            get => null;
            set
            {
                if (value is { Count: > 0 })
                    Reversal = value;
            }
        }
    }

    public class CandidateSummaryItem
    {
        public required string Ticker { get; set; }
    }
}
