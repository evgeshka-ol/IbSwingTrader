namespace IbSwingTrader.Models
{
    public class EvaluateCandidatesFolderSettings
    {
        public string SearchPattern { get; set; } = "candidates_*.json";
        public int ConnectTimeoutSeconds { get; set; } = 15;
    }
}
