
namespace IbSwingTrader.Abstractions.Dataset
{
    public interface ICsvTradeReader
    {
        List<TradeRecord> Read(string path);
    }
}