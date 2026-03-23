using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ICsvTradeReader
    {
        List<TradeRecord> Read(string path);
    }
}