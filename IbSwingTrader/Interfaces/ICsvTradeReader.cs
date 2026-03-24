using IbSwingTrader.Models.Stocks;

namespace IbSwingTrader.Interfaces
{
    public interface ICsvTradeReader
    {
        List<TradeRecord> Read(string path);
    }
}