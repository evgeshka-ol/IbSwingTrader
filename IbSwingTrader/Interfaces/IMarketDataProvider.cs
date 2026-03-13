using IBApi;
using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IMarketDataProvider
    {
        Task<List<Candle>> GetCandles(
                    Contract contract,
                    Timeframe timeframe,
                    DateTime endTimeUtc,
                    int bars);

        Task<List<Candle>> GetHistoricalRange(
           Contract contract,
           Timeframe timeframe,
           DateTime start,
           DateTime end);
    }
}
