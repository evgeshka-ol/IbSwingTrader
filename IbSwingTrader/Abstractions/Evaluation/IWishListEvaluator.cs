
namespace IbSwingTrader.Abstractions.Evaluation
{
    public interface IWishListEvaluator
    {
        Task<List<WishListEvaluationResult>> EvaluateAsync(List<WishListItem> items);
    }
}