using IbSwingTrader.Models;
using IbSwingTrader.Models.Stocks;

namespace IbSwingTrader.Interfaces
{
    public interface ITradeDatasetBuilder
    {
        List<TradeDatasetRow> Build(List<TradeRecord> trades, List<Candle> candles);
    }
}
