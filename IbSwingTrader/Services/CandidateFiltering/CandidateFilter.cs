using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateFiltering
{
    public class CandidateFilter : ICandidateFilter
    {
        public bool Pass(FeatureSet f)
        {
            if (f.VolumeRatio20 < 1.2m)
                return false;

            if (f.DistanceTo20dHigh < -15m)
                return false;

            if (f.ATRRatio > 0.12m)
                return false;

            return true;
        }
    }
}
