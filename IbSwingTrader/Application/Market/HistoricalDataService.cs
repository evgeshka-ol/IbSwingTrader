using IBApi;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Application.Market
{
    public class HistoricalDataService(
        IMarketDataProvider provider,
        IHistoricalRequestThrottler throttler,
        IHistoricalCache cache,
        IHistoricalRetryPolicy retryPolicy,
        ITextLogger logger,
        IMarketGapAnalyzer marketGapAnalyzer,
        IMarketCoverageService marketCoverageService) : IHistoricalDataService
    {
        private readonly IMarketDataProvider _provider = provider;
        private readonly IHistoricalRequestThrottler _throttler = throttler;
        private readonly IHistoricalCache _cache = cache;
        private readonly IHistoricalRetryPolicy _retryPolicy = retryPolicy;
        private readonly ITextLogger _logger = logger;
        private readonly IMarketGapAnalyzer _marketGapAnalyzer = marketGapAnalyzer;
        private readonly IMarketCoverageService _marketCoverageService = marketCoverageService;

        public async Task<List<Candle>?> GetCandlesRange(
            string symbol,
            Contract contract,
            Timeframe timeframe,
            DateTime start,
            DateTime end)
        {
            if (end <= start)
                return [];

            var normalizedStart = NormalizeRangeStart(start, timeframe);
            var normalizedEnd = NormalizeRangeEnd(end, timeframe);

            if (normalizedEnd <= normalizedStart)
                return [];

            var expectedStep = GetExpectedStep(timeframe);
            var overlap = expectedStep;

            List<Candle> allCandles = [];

            var hasCache = _cache.TryLoad(symbol, timeframe, out var cached) &&
                           cached != null &&
                           cached.Count > 0;

            if (hasCache)
            {
                allCandles = MergeCandles(cached!);

                _logger.Debug(
                    $"Historical cache hit: {symbol}, tf={timeframe}, candles={allCandles.Count}, " +
                    $"first={allCandles.First().Time:yyyy-MM-dd HH:mm:ss}, last={allCandles.Last().Time:yyyy-MM-dd HH:mm:ss}");
            }

            var missingRanges = await BuildMissingRangesAsync(
                contract,
                timeframe,
                allCandles,
                normalizedStart,
                normalizedEnd,
                overlap);

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

                    if (loaded.Count > 0)
                        allCandles.AddRange(loaded);
                }

                allCandles = MergeCandles(allCandles);
                _cache.Save(symbol, timeframe, allCandles);
            }

            var result = allCandles
                .Where(x => x.Time >= normalizedStart && x.Time <= normalizedEnd)
                .OrderBy(x => x.Time)
                .ToList();

            await LogCoverageAsync(
                symbol,
                contract,
                timeframe,
                normalizedStart,
                normalizedEnd,
                result,
                expectedStep);

            if (result.Count <= 2)
            {
                _logger.Error(
                    $"Historical result too small: {symbol}, tf={timeframe}, start={normalizedStart:yyyy-MM-dd HH:mm:ss}, end={normalizedEnd:yyyy-MM-dd HH:mm:ss}, candles={result.Count}");
            }

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
                {
                    allCandles.AddRange(chunkCandles);
                }
                else if (timeframe == Timeframe.H4 && allCandles.Count == 0)
                {
                    _logger.Error(
                        $"Historical range aborted after first failed chunk: {symbol}, tf={timeframe}, start={currentChunkStart:yyyy-MM-dd HH:mm:ss}, end={currentChunkEnd:yyyy-MM-dd HH:mm:ss}");
                    break;
                }

                chunkStart = chunkEnd;
            }

            return MergeCandles(allCandles);
        }

        private async Task<List<DateRange>> BuildMissingRangesAsync(
            Contract contract,
            Timeframe timeframe,
            List<Candle> candles,
            DateTime requestedStart,
            DateTime requestedEnd,
            TimeSpan overlap)
        {
            if (candles.Count == 0)
            {
                return
                [
                    new DateRange(requestedStart, requestedEnd)
                ];
            }

            var ordered = candles
                .Where(x => x.Time >= requestedStart - overlap && x.Time <= requestedEnd + overlap)
                .OrderBy(x => x.Time)
                .ToList();

            if (ordered.Count == 0)
            {
                return
                [
                    new DateRange(requestedStart, requestedEnd)
                ];
            }

            var ranges = new List<DateRange>();
            var cachedStart = ordered.First().Time;
            var cachedEnd = ordered.Last().Time;

            if (requestedStart < cachedStart)
            {
                var leftEnd = Min(requestedEnd, cachedStart + overlap);

                if (requestedStart < leftEnd)
                {
                    var hasExpectedBars = await _marketCoverageService.HasExpectedBarsBetweenAsync(
                        contract,
                        timeframe,
                        MarketTime.ToUtc(requestedStart),
                        MarketTime.ToUtc(leftEnd));

                    if (hasExpectedBars)
                        ranges.Add(new DateRange(requestedStart, leftEnd));
                }
            }

            if (requestedEnd > cachedEnd)
            {
                var rightStart = Max(requestedStart, cachedEnd - overlap);

                if (rightStart < requestedEnd)
                {
                    var hasExpectedBars = await _marketCoverageService.HasExpectedBarsBetweenAsync(
                        contract,
                        timeframe,
                        MarketTime.ToUtc(rightStart),
                        MarketTime.ToUtc(requestedEnd));

                    if (hasExpectedBars)
                        ranges.Add(new DateRange(rightStart, requestedEnd));
                }
            }

            return MergeRanges(ranges);
        }

        private async Task LogCoverageAsync(
            string symbol,
            Contract contract,
            Timeframe timeframe,
            DateTime requestedStart,
            DateTime requestedEnd,
            List<Candle> candles,
            TimeSpan expectedStep)
        {
            if (candles.Count == 0)
            {
                _logger.Error(
                    $"Historical result empty: {symbol}, tf={timeframe}, start={requestedStart:yyyy-MM-dd HH:mm:ss}, end={requestedEnd:yyyy-MM-dd HH:mm:ss}");
                return;
            }

            var gaps = await FindGapsAsync(contract, candles, expectedStep, timeframe);

            _logger.Debug(
                $"Historical result ready: {symbol}, tf={timeframe}, candles={candles.Count}, " +
                $"first={candles.First().Time:yyyy-MM-dd HH:mm:ss}, last={candles.Last().Time:yyyy-MM-dd HH:mm:ss}");

            if (gaps.Count > 0)
            {
                var message =
                    $"Gaps detected: {gaps.Count}, symbol={symbol}, tf={timeframe}, " +
                    $"examples={string.Join("; ", gaps.Take(5).Select(x => $"{x.Start:MM-dd HH:mm}->{x.End:MM-dd HH:mm}"))}";

                if (IsIntraday(timeframe))
                    _logger.Debug(message);
                else
                    _logger.Error(message);
            }
        }

        private async Task<List<DateRange>> FindGapsAsync(
            Contract contract,
            List<Candle> candles,
            TimeSpan expectedStep,
            Timeframe timeframe)
        {
            var ordered = candles
                .OrderBy(x => x.Time)
                .ToList();

            var result = new List<DateRange>();

            if (ordered.Count <= 1)
                return result;

            var tolerance = GetGapTolerance(expectedStep);

            for (int i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1].Time;
                var current = ordered[i].Time;
                var diff = current - prev;

                var isExpectedGap = await _marketGapAnalyzer.IsExpectedGapAsync(
                    contract,
                    timeframe,
                    MarketTime.ToUtc(prev),
                    MarketTime.ToUtc(current));

                if (isExpectedGap)
                    continue;

                if (diff <= expectedStep + tolerance)
                    continue;

                var missingBars = (int)Math.Round(diff.TotalSeconds / expectedStep.TotalSeconds) - 1;

                if (IsIntraday(timeframe) && missingBars < 3)
                    continue;

                result.Add(new DateRange(prev, current));
            }

            return result;
        }

        private static bool IsIntraday(Timeframe timeframe)
        {
            return timeframe == Timeframe.H4 ||
                   timeframe == Timeframe.H1 ||
                   timeframe == Timeframe.M30 ||
                   timeframe == Timeframe.M15 ||
                   timeframe == Timeframe.M5 ||
                   timeframe == Timeframe.M1;
        }

        private static List<Candle> MergeCandles(List<Candle> candles)
        {
            return candles
                .GroupBy(x => new { x.Timeframe, x.Time })
                .Select(g => g.Last())
                .OrderBy(x => x.Time)
                .ToList();
        }

        private static List<DateRange> MergeRanges(List<DateRange> ranges)
        {
            if (ranges.Count == 0)
                return [];

            var ordered = ranges
                .OrderBy(x => x.Start)
                .ToList();

            var result = new List<DateRange>
            {
                ordered[0]
            };

            for (int i = 1; i < ordered.Count; i++)
            {
                var last = result[^1];
                var current = ordered[i];

                if (current.Start <= last.End)
                {
                    result[^1] = new DateRange(
                        last.Start,
                        Max(last.End, current.End));
                }
                else
                {
                    result.Add(current);
                }
            }

            return result;
        }

        private static DateTime NormalizeRangeStart(DateTime value, Timeframe timeframe)
        {
            return timeframe switch
            {
                Timeframe.H4 => RoundDown(value, TimeSpan.FromHours(4)),
                Timeframe.H1 => RoundDown(value, TimeSpan.FromHours(1)),
                Timeframe.M30 => RoundDown(value, TimeSpan.FromMinutes(30)),
                Timeframe.M15 => RoundDown(value, TimeSpan.FromMinutes(15)),
                Timeframe.M5 => RoundDown(value, TimeSpan.FromMinutes(5)),
                Timeframe.M1 => RoundDown(value, TimeSpan.FromMinutes(1)),
                Timeframe.D1 => value.Date,
                Timeframe.W1 => StartOfWeek(value.Date),
                _ => value
            };
        }

        private static DateTime NormalizeRangeEnd(DateTime value, Timeframe timeframe)
        {
            return timeframe switch
            {
                Timeframe.H4 => RoundDown(value, TimeSpan.FromHours(4)),
                Timeframe.H1 => RoundDown(value, TimeSpan.FromHours(1)),
                Timeframe.M30 => RoundDown(value, TimeSpan.FromMinutes(30)),
                Timeframe.M15 => RoundDown(value, TimeSpan.FromMinutes(15)),
                Timeframe.M5 => RoundDown(value, TimeSpan.FromMinutes(5)),
                Timeframe.M1 => RoundDown(value, TimeSpan.FromMinutes(1)),
                Timeframe.D1 => value.Date,
                Timeframe.W1 => StartOfWeek(value.Date),
                _ => value
            };
        }

        private static DateTime RoundDown(DateTime value, TimeSpan step)
        {
            var ticks = value.Ticks / step.Ticks * step.Ticks;
            return new DateTime(ticks, value.Kind);
        }

        private static DateTime StartOfWeek(DateTime value)
        {
            var diff = (7 + (value.DayOfWeek - DayOfWeek.Monday)) % 7;
            return value.AddDays(-diff).Date;
        }

        private static TimeSpan GetExpectedStep(Timeframe timeframe)
        {
            return timeframe switch
            {
                Timeframe.M1 => TimeSpan.FromMinutes(1),
                Timeframe.M5 => TimeSpan.FromMinutes(5),
                Timeframe.M15 => TimeSpan.FromMinutes(15),
                Timeframe.M30 => TimeSpan.FromMinutes(30),
                Timeframe.H1 => TimeSpan.FromHours(1),
                Timeframe.H4 => TimeSpan.FromHours(4),
                Timeframe.D1 => TimeSpan.FromDays(1),
                Timeframe.W1 => TimeSpan.FromDays(7),
                _ => TimeSpan.FromHours(4)
            };
        }

        private static TimeSpan GetGapTolerance(TimeSpan expectedStep)
        {
            if (expectedStep <= TimeSpan.FromMinutes(5))
                return TimeSpan.FromMinutes(2);

            if (expectedStep <= TimeSpan.FromHours(1))
                return TimeSpan.FromMinutes(20);

            if (expectedStep <= TimeSpan.FromHours(4))
                return TimeSpan.FromHours(6);

            if (expectedStep <= TimeSpan.FromDays(1))
                return TimeSpan.FromHours(36);

            return TimeSpan.FromDays(3);
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
