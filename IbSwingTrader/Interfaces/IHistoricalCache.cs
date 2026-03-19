using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    namespace IbSwingTrader.Interfaces
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
}
