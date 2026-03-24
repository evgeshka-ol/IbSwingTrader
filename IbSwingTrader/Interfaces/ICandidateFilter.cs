using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICandidateFilter
    {
        bool Pass(
            CandidateSignalSnapshot snapshot,
            decimal price,
            decimal avgDollarVolumeDaily20);
    }
}
