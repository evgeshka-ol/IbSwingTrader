using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IStockPreFilter
    {
        bool Pass(StockInfo stock);
    }
}
