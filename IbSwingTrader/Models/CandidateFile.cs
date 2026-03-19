namespace IbSwingTrader.Models
{
    public class CandidateFile
    {
        public DateTime GeneratedAtNy { get; set; }

        public string TimeZone { get; set; } = "America/New_York";

        public List<CandidateDetails> Candidates { get; set; } = [];
    }
}