using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Interfaces
{
    public interface IWishListMerger
    {
        List<WishListItem> Merge(
            List<WishListItem> currentItems,
            List<WishListItem> newItems,
            DateTime marketNow);
    }
}