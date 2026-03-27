
namespace IbSwingTrader.Abstractions.WishList
{
    public interface IWishListScore
    {
        WishListScoreResult Calculate(CandidateSignalSnapshot snapshot);
    }
}