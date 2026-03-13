using IBApi;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services;

public class HistoricalDataService(
    IMarketDataProvider provider,
    IHistoricalRequestThrottler throttler,
    IHistoricalCache cache,
    IHistoricalRetryPolicy retryPolicy,
    ILogger logger) : IHistoricalDataService
{
    private readonly IMarketDataProvider _provider = provider;
    private readonly IHistoricalRequestThrottler _throttler = throttler;
    private readonly IHistoricalCache _cache = cache;
    private readonly IHistoricalRetryPolicy _retryPolicy = retryPolicy;
    private readonly ILogger _logger = logger;

    public async Task<List<Candle>?> GetCandlesRange(
        string symbol,
        Contract contract,
        Timeframe timeframe,
        DateTime start,
        DateTime end)
    {
        // CACHE
        if (_cache.TryLoad(symbol, out var cached))
        {
            _logger.Debug($"Historical cache hit: {symbol}");
            return cached;
        }

        var candles = await _retryPolicy.ExecuteAsync(async () =>
        {
            using (await _throttler.AcquireAsync())
            {
                return await _provider.GetHistoricalRange(
                    contract,
                    timeframe,
                    start,
                    end);
            }
        });

        if (candles == null || candles.Count == 0)
            return candles;

        _cache.Save(symbol, candles);

        return candles;
    }
}