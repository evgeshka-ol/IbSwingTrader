using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Analysis
{
    public class CandidateScore : ICandidateScore
    {
        public decimal Calculate(FeatureSet f)
        {
            decimal score =
                (-f.DistanceTo20dHigh * 0.35m)
                + (-f.Pullback10d * 0.25m)
                + (f.VolumeRatio20 * 0.15m)
                + ((0.08m - f.ATRRatio) * 100 * 0.15m)
                + ((1 - f.TrendPosition) * 100 * 0.1m);

            return score;
        }
    }
}
