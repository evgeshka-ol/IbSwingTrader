namespace IbSwingTrader.Models.Settings
{
    public class ResearchSettings
    {
        public List<int> PullbackWindows { get; set; } = [];
        public List<int> VolumeWindows { get; set; } = [];
        public List<int> DistanceToHighWindows { get; set; } = [];
        public List<int> FutureHorizons { get; set; } = [];
        public List<decimal> TargetPercents { get; set; } = [];
    }
}