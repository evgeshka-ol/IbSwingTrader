using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IWishListFilter
    {
        bool Pass(CandidateSignalSnapshot snapshot, decimal price, decimal avgDollarVolumeDaily20);
    }
}
