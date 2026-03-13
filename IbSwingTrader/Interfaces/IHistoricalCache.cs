using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IHistoricalCache
    {
        bool TryLoad(string symbol, out List<Candle>? candles);
        void Save(string symbol, List<Candle> candles);
    }
}
