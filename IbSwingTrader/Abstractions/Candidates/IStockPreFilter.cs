
namespace IbSwingTrader.Abstractions.Candidates
{
    public interface IStockPreFilter
    {
        bool Pass(StockInfo stock);
    }
}
