using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Interfaces
{
    public interface IWishListReader
    {
        Task<List<WishListItem>> ReadAsync(string filePath);
    }
}