using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Models
{
    public class AgentSettings
    {
        public int ConfigVersion { get; set; } = 1;

        public PathSettings Paths { get; set; } = new();
        public TwsSettings Tws { get; set; } = new();
        public MarketSettings Market { get; set; } = new();

        public BuildDatasetSettings BuildDataset { get; set; } = new();
        public FeatureCalculationSettings FeatureCalculation { get; set; } = new();

        public GetCandidatesSettings GetCandidates { get; set; } = new();
        public EvaluationSettings Evaluation { get; set; } = new();
    }
}