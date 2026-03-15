using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateScore
    {
        decimal Calculate(FeatureSet f);
    }
}
