namespace IbSwingTrader.Domain.Settings
{
    public class CandidateEvaluationSettings
    {
        public string SearchPattern { get; set; } = "candidates_*.json";
        public int ForwardEvaluationDays { get; set; } = 7;
        public int FreshDataSafetyLagMinutes { get; set; } = 10;
        public decimal DefaultTargetPct { get; set; } = 10m;
        public bool UseAmbiguousBarResolver { get; set; } = true;
        public bool ReevaluateOpenCandidates { get; set; } = false;
    }
}
