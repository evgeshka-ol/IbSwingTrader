
namespace IbSwingTrader.Abstractions.WishList
{
    public interface IWishListResultWriter
    {
        Task WriteAsync(string filePath, List<WishListItem> items);
    }
}