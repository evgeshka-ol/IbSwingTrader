
namespace IbSwingTrader.Abstractions.WishList
{
    public interface IWishListReader
    {
        Task<List<WishListItem>> ReadAsync(string filePath);
    }
}