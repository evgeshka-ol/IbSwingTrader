using IBApi;

namespace IbSwingTrader.Abstractions.Market
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
