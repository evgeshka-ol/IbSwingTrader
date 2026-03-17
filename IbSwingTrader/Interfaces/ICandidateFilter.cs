using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateFilter
    {
        bool Pass(
            FeatureSet features,
            decimal price,
            decimal avgVolume20,
            decimal marketCap);
    }
}
