using IBApi;

namespace IbSwingTrader.Infrastructure.Brokers.InteractiveBrokers
{
    public class TwsMarketDataProvider(ITwsConnection tws, ITextLogger logger) : IMarketDataProvider
    {
        private readonly ITwsConnection _tws = tws;
        private readonly ITextLogger _logger = logger;

        public Task<List<Candle>> GetCandles(
            Contract contract,
            Timeframe timeframe,
            DateTime endTimeUtc,
            int bars)
        {
            return _tws.RequestHistoricalData(
                contract,
                timeframe,
                endTimeUtc,
                bars);
        }

        public async Task<List<Candle>> GetHistoricalRange(
            Contract contract,
            Timeframe timeframe,
            DateTime start,
            DateTime end)
        {
            var result = new List<Candle>();

            if (end <= start)
                return result;

            var tfSpan = timeframe.ToTimeSpan();
            var maxBarsPerRequest = timeframe.GetMaxBarsPerRequest();

            var cursor = end;

            while (cursor > start)
            {
                var remainingSpan = cursor - start;
                var remainingBars = (int)Math.Ceiling(remainingSpan.TotalSeconds / tfSpan.TotalSeconds);

                var barsToRequest = Math.Min(maxBarsPerRequest, remainingBars);
                if (barsToRequest <= 0)
                    break;

                _logger.Debug(
                    $"Historical range chunk: tf={timeframe}, cursor={cursor:yyyy-MM-dd HH:mm:ss}, bars={barsToRequest}");

                var chunk = await GetCandles(
                    contract,
                    timeframe,
                    cursor,
                    barsToRequest);

                if (chunk.Count == 0)
                    break;

                result.AddRange(chunk);

                var earliest = chunk.Min(x => x.Time);

                if (earliest >= cursor)
                    break;

                cursor = earliest.AddSeconds(-1);

                await Task.Delay(300);
            }

            var candles = result
                .Where(c => c.Time >= start && c.Time <= end)
                .GroupBy(c => new { c.Timeframe, c.Time })
                .Select(g => g.First())
                .OrderBy(c => c.Time)
                .ToList();

            _logger.Debug($"Total candles: {candles.Count}");

            int gaps = 0;
            DateTime? firstGapStart = null;
            DateTime? lastGapEnd = null;

            for (int i = 1; i < candles.Count; i++)
            {
                var diff = candles[i].Time - candles[i - 1].Time;

                if (diff > TimeSpan.FromHours(8))
                {
                    gaps++;

                    if (firstGapStart == null)
                        firstGapStart = candles[i - 1].Time;

                    lastGapEnd = candles[i].Time;
                }
            }

            if (gaps > 0)
            {
                _logger.Debug(
                    $"Gaps detected: {gaps} " +
                    $"({firstGapStart:yyyy-MM-dd HH:mm} -> {lastGapEnd:yyyy-MM-dd HH:mm})");
            }

            return candles;
        }
    }
}
