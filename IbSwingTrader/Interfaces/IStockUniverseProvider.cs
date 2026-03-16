using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IStockUniverseProvider
    {
        Task<List<StockInfo>> GetStocksAsync();
    }
}
