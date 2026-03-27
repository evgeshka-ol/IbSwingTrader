
namespace IbSwingTrader.Abstractions.Market
{
    public interface IStockUniverseProvider
    {
        Task<List<StockInfo>> GetStocksAsync(string scanCode);
    }
}
