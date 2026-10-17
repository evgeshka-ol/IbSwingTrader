using IBApi;

namespace IbSwingTrader.Abstractions.Market;

public interface IHistoricalDataService
{
    // Exact five-minute parent window, without range gap expansion/overlap.
    Task<List<Candle>?> GetM1InsideM5(string symbol, Contract contract, DateTime start)
        => Task.FromResult<List<Candle>?>(null);
    // Explicit fresh snapshot, including the forming bar, without cache substitution.
    Task<List<Candle>> GetFreshM5Snapshot(
        string symbol, Contract contract, DateTime start, DateTime requestedAt);

    Task<List<Candle>?> GetCandlesRange(
        string symbol,
        Contract contract,
        Timeframe timeframe,
        DateTime start,
        DateTime end,
        int maxConsecutiveEmptyChunks = 0);
}
