namespace IbSwingTrader.Domain.Settings
{
    public class AgentSettings
    {
        public int ConfigVersion { get; set; } = 1;

        public PathSettings Paths { get; set; } = new();
        public TwsSettings Tws { get; set; } = new();
        public MarketSettings Market { get; set; } = new();
        public MarketSessionsSettings MarketSessions { get; set; } = new();

        public BuildDatasetSettings BuildDataset { get; set; } = new();
        public FeatureCalculationSettings FeatureCalculation { get; set; } = new();

        public GetCandidatesSettings GetCandidates { get; set; } = new();
        public CandidateEvaluationSettings CandidateEvaluation { get; set; } = new();
        public WishListEvaluationSettings WishListEvaluation { get; set; } = new();
        public CleanUpSettings CleanUp { get; set; } = new();

        public CsvTradeReaderSettings CsvTradeReader { get; set; } = new();
        public LoggingSettings Logging { get; set; } = new();
    }
}
