using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ITradeDatasetBuilder
    {
        List<TradeDatasetRow> Build(List<TradeRecord> trades, List<Candle> candles);
    }
}
