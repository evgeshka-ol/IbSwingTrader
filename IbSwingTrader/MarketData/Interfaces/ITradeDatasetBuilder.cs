using IbSwingTrader.Models;

namespace IbSwingTrader.MarketData.Interfaces
{
    public interface ITradeDatasetBuilder
    {
        List<TradeDatasetRow> Build(List<TradeRecord> trades, List<Candle> candles);
    }
}
