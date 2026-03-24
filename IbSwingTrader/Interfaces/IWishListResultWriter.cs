using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Interfaces
{
    public interface IWishListResultWriter
    {
        Task WriteAsync(string filePath, List<WishListItem> items);
    }
}