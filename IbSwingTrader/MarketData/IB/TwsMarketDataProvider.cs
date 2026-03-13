using IBApi;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.MarketData.IB
{
    public class TwsMarketDataProvider(ITwsConnection tws, ILogger logger) : IMarketDataProvider
    {
        private readonly ITwsConnection _tws = tws;
        private readonly ILogger _logger = logger;

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

            var cursor = end;

            while (cursor > start)
            {
                var chunk = await GetCandles(
                    contract,
                    timeframe,
                    cursor,
                    300 * 6);

                if (chunk.Count == 0)
                    break;

                result.AddRange(chunk);

                var earliest = chunk.Min(x => x.Time);
                cursor = earliest.AddSeconds(-1);

                await Task.Delay(300);
            }

            var candles = result
                .Where(c => c.Time >= start && c.Time <= end)
                .GroupBy(c => c.Time)            // remove duplicates
                .Select(g => g.First())
                .OrderBy(c => c.Time)
                .ToList();

            _logger.Debug($"Total candles: {candles.Count}");

            for (int i = 1; i < candles.Count; i++)
            {
                var diff = candles[i].Time - candles[i - 1].Time;

                if (diff > TimeSpan.FromHours(8))
                {
                    _logger.Debug($"Gap detected: {candles[i - 1].Time} -> {candles[i].Time}");
                }
            }

            return candles;
        }
    }
}
