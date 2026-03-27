
namespace IbSwingTrader.Abstractions.Market
{
    public interface IHistoricalCache
    {
        bool TryLoad(
            string symbol,
            Timeframe timeframe,
            out List<Candle>? candles);

        void Save(
            string symbol,
            Timeframe timeframe,
            List<Candle> candles);
    }
}