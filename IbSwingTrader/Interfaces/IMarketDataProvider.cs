using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IMarketDataProvider
    {
        Task<List<Candle>> GetCandles(
            string ticker,
            Timeframe timeframe,
            DateTime endTimeUtc,
            int bars);

        Task<List<Candle>> GetHistoricalRange(
           string ticker,
           Timeframe timeframe,
           DateTime start,
           DateTime end);
    }
}
