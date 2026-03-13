using IBApi;
using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces;

public interface IHistoricalDataService
{
    Task<List<Candle>?> GetCandlesRange(
        string symbol,
        Contract contract,
        Timeframe timeframe,
        DateTime start,
        DateTime end);
}