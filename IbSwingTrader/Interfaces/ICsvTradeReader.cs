using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Interfaces
{
    public interface ICsvTradeReader
    {
        List<TradeRecord> Read(string path);
    }
}