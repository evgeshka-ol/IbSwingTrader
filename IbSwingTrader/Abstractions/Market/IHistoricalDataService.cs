using IBApi;

namespace IbSwingTrader.Abstractions.Market;

public interface IHistoricalDataService
{
    // Explicit fresh snapshot, including the forming bar, without cache substitution.
    Task<List<Candle>> GetFreshM5Snapshot(
        string symbol, Contract contract, DateTime start, DateTime requestedAt);

    Task<List<Candle>?> GetCandlesRange(
        string symbol,
        Contract contract,
        Timeframe timeframe,
        DateTime start,
        DateTime end);
}
