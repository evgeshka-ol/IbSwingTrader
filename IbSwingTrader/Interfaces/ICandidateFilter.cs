using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateFilter
    {
        bool Pass(FeatureSet f);
    }
}
