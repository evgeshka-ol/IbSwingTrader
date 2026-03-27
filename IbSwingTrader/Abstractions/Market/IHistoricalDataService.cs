using IBApi;

namespace IbSwingTrader.Abstractions.Market;

public interface IHistoricalDataService
{
    Task<List<Candle>?> GetCandlesRange(
        string symbol,
        Contract contract,
        Timeframe timeframe,
        DateTime start,
        DateTime end);
}