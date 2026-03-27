
namespace IbSwingTrader.Abstractions.WishList
{
    public interface IWishListFilter
    {
        bool Pass(CandidateSignalSnapshot snapshot, decimal price, decimal avgDollarVolumeDaily20);
    }
}
