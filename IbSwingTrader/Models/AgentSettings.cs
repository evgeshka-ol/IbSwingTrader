namespace IbSwingTrader.Models
{
    public class AgentSettings
    {
        public int ConfigVersion { get; set; } = 1;
        public PathSettings Paths { get; set; } = new();
        public IbkrSettings Ibkr { get; set; } = new();
        public MarketSettings Market { get; set; } = new();
        public ResearchSettings Research { get; set; } = new();
        public BuildDatasetSettings BuildDataset { get; set; } = new();
        public GetCandidatesSettings GetCandidates { get; set; } = new();
        public ScoringSettings Scoring { get; set; } = new();
        public WishListSettings WishList { get; set; } = new();
        public EvaluationSettings Evaluation { get; set; } = new();
        public LoggingSettings Logging { get; set; } = new();
    }
}