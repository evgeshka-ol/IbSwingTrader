using IbSwingTrader.Models;
using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Interfaces
{
    public interface ITradeDatasetBuilder
    {
        List<TradeDatasetRow> Build(List<TradeRecord> trades, List<Candle> candles);
    }
}
