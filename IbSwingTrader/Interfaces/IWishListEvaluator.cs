using IbSwingTrader.Models;
using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Interfaces
{
    public interface IWishListEvaluator
    {
        Task<List<WishListEvaluationResult>> EvaluateAsync(List<WishListItem> items);
    }
}