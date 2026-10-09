
namespace IbSwingTrader.Abstractions.Market
{
    public interface IHistoricalCache
    {
        // Null means version tracking is unavailable; consumers must not memoize disk-derived data.
        string? GetVersion(string symbol) => null;

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
