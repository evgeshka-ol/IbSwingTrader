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

            var candles = result
                .Where(c => c.Time >= start && c.Time <= end)
                .GroupBy(c => c.Time)            // remove duplicates
                .Select(g => g.First())
                .OrderBy(c => c.Time)
                .ToList();

            Console.WriteLine($"Total candles: {candles.Count}");

            for (int i = 1; i < candles.Count; i++)
            {
                var diff = candles[i].Time - candles[i - 1].Time;

                if (diff > TimeSpan.FromHours(8))
                {
                    Console.WriteLine($"Gap detected: {candles[i - 1].Time} -> {candles[i].Time}");
                }
            }

            return candles;
        }
    }
}
