using IBApi;
using IbSwingTrader.Extensions;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services
{
    public class HistoricalDataService(
        IMarketDataProvider provider,
        IHistoricalRequestThrottler throttler,
        IHistoricalCache cache,
        IHistoricalRetryPolicy retryPolicy,
        ITextLogger logger) : IHistoricalDataService
    {
        private readonly IMarketDataProvider _provider = provider;
        private readonly IHistoricalRequestThrottler _throttler = throttler;
        private readonly IHistoricalCache _cache = cache;
        private readonly IHistoricalRetryPolicy _retryPolicy = retryPolicy;
        private readonly ITextLogger _logger = logger;

        public async Task<List<Candle>?> GetCandlesRange(
            string symbol,
            Contract contract,
            Timeframe timeframe,
            DateTime start,
            DateTime end)
        {
            if (end <= start)
                return [];

            var cacheKey = BuildCacheKey(symbol, timeframe, start, end);

            if (_cache.TryLoad(cacheKey, out var cached))
            {
                _logger.Debug($"Historical cache hit: {cacheKey}");
                return cached;
            }

            var chunkSpan = timeframe.GetMaxRequestSpan();
            var allCandles = new List<Candle>();

            var chunkStart = start;

            while (chunkStart < end)
            {
                var chunkEnd = chunkStart.Add(chunkSpan);
                if (chunkEnd > end)
                    chunkEnd = end;

                var currentChunkStart = chunkStart;
                var currentChunkEnd = chunkEnd;

                _logger.Debug(
                    $"Historical chunk load: {symbol}, tf={timeframe}, start={currentChunkStart:yyyy-MM-dd HH:mm:ss}, end={currentChunkEnd:yyyy-MM-dd HH:mm:ss}");

                var chunkCandles = await _retryPolicy.ExecuteAsync(async () =>
                {
                    using (await _throttler.AcquireAsync())
                    {
                        return await _provider.GetHistoricalRange(
                            contract,
                            timeframe,
                            currentChunkStart,
                            currentChunkEnd);
                    }
                });

                if (chunkCandles != null && chunkCandles.Count > 0)
                    allCandles.AddRange(chunkCandles);

                chunkStart = chunkEnd;
            }

            var merged = allCandles
                .GroupBy(x => new { x.Timeframe, x.Time })
                .Select(g => g.First())
                .OrderBy(x => x.Time)
                .ToList();

            _cache.Save(cacheKey, merged);

            return merged;
        }

        private static string BuildCacheKey(
            string symbol,
            Timeframe timeframe,
            DateTime start,
            DateTime end)
        {
            return $"{symbol}|{timeframe}|{start:O}|{end:O}";
        }
    }
}
