using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IWishListScore
    {
        decimal Calculate(CandidateSignalSnapshot snapshot);
    }
}
