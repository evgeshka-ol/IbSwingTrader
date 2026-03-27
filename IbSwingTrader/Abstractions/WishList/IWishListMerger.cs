
namespace IbSwingTrader.Abstractions.WishList
{
    public interface IWishListMerger
    {
        List<WishListItem> Merge(
            List<WishListItem> currentItems,
            List<WishListItem> newItems);
    }
}