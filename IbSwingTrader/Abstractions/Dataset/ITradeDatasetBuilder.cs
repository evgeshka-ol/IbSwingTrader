
namespace IbSwingTrader.Abstractions.Dataset
{
    public interface ITradeDatasetBuilder
    {
        List<TradeDatasetRow> Build(List<TradeRecord> trades, List<Candle> candles);
    }
}
