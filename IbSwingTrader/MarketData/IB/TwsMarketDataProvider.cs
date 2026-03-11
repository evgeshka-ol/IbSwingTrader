using IbSwingTrader.MarketData.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.MarketData.IB
{
    public class TwsMarketDataProvider(TwsConnection tws) : IMarketDataProvider
    {
        private readonly TwsConnection _tws = tws;

        public Task<List<Candle>> GetCandles(
            string ticker,
            Timeframe timeframe,
            DateTime endTimeUtc,
            int bars)
        {
            return _tws.RequestHistoricalData(
                ticker,
                timeframe,
                endTimeUtc,
                bars);
        }

        public async Task<List<Candle>> GetHistoricalRange(
           string ticker,
           Timeframe timeframe,
           DateTime start,
           DateTime end)
        {
            var result = new List<Candle>();

            var cursor = end;

            while (cursor > start)
            {
                var chunk = await GetCandles(
                    ticker,
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

            return [.. result
                .Where(c => c.Time >= start && c.Time <= end)
                .OrderBy(c => c.Time)];
        }
    }
}
