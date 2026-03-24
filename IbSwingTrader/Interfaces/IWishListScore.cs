using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IWishListScore
    {
        WishListScoreResult Calculate(CandidateSignalSnapshot snapshot);
    }
}