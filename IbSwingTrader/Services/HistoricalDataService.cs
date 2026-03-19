using IBApi;
using IbSwingTrader.Extensions;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Interfaces.IbSwingTrader.Interfaces;
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

            List<Candle> allCandles = [];

            var hasCache = _cache.TryLoad(symbol, timeframe, out var cached) &&
                           cached != null &&
                           cached.Count > 0;

            if (hasCache)
            {
                allCandles = cached!;
                _logger.Debug(
                    $"Historical cache hit: {symbol}, tf={timeframe}, candles={allCandles.Count}");
            }

            var missingRanges = BuildMissingRanges(allCandles, start, end);

            if (missingRanges.Count > 0)
            {
                foreach (var range in missingRanges)
                {
                    _logger.Debug(
                        $"Historical cache miss: {symbol}, tf={timeframe}, start={range.Start:yyyy-MM-dd HH:mm:ss}, end={range.End:yyyy-MM-dd HH:mm:ss}");

                    var loaded = await LoadRangeFromIb(
                        symbol,
                        contract,
                        timeframe,
                        range.Start,
                        range.End);

                    if (loaded != null && loaded.Count > 0)
                        allCandles.AddRange(loaded);
                }

                allCandles = MergeCandles(allCandles);
                _cache.Save(symbol, timeframe, allCandles);
            }

            var result = allCandles
                .Where(x => x.Time >= start && x.Time <= end)
                .OrderBy(x => x.Time)
                .ToList();

            return result;
        }

        private async Task<List<Candle>> LoadRangeFromIb(
            string symbol,
            Contract contract,
            Timeframe timeframe,
            DateTime start,
            DateTime end)
        {
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

            return MergeCandles(allCandles);
        }

        private static List<DateRange> BuildMissingRanges(
            List<Candle> candles,
            DateTime requestedStart,
            DateTime requestedEnd)
        {
            if (candles.Count == 0)
            {
                return
                [
                    new DateRange(requestedStart, requestedEnd)
                ];
            }

            var ordered = candles
                .OrderBy(x => x.Time)
                .ToList();

            var cachedStart = ordered.First().Time;
            var cachedEnd = ordered.Last().Time;

            var ranges = new List<DateRange>();

            if (requestedStart < cachedStart)
            {
                var leftEnd = Min(requestedEnd, cachedStart);

                if (requestedStart < leftEnd)
                    ranges.Add(new DateRange(requestedStart, leftEnd));
            }

            if (requestedEnd > cachedEnd)
            {
                var rightStart = Max(requestedStart, cachedEnd);

                if (rightStart < requestedEnd)
                    ranges.Add(new DateRange(rightStart, requestedEnd));
            }

            return ranges;
        }

        private static List<Candle> MergeCandles(List<Candle> candles)
        {
            return candles
                .GroupBy(x => new { x.Timeframe, x.Time })
                .Select(g => g.First())
                .OrderBy(x => x.Time)
                .ToList();
        }

        private static DateTime Min(DateTime a, DateTime b)
        {
            return a <= b ? a : b;
        }

        private static DateTime Max(DateTime a, DateTime b)
        {
            return a >= b ? a : b;
        }

        private readonly record struct DateRange(DateTime Start, DateTime End);
    }
}