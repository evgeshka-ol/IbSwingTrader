
namespace IbSwingTrader.Abstractions.Candidates
{
    public interface ITradeBuilder
    {
        TradePlan Build(List<Candle> candles, List<Candle>? entryCandles = null);
    }
}
