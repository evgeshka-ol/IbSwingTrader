using System.Collections.Concurrent;
using IBApi;
using IbSwingTrader.Extensions;
using IbSwingTrader.MarketData.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.MarketData.IB
{
    public class TwsMarketDataProvider(EClientSocket client) : IMarketDataProvider
    {
        private readonly EClientSocket _client = client;

        private int _nextRequestId = 1;

        private readonly ConcurrentDictionary<int, TaskCompletionSource<List<Candle>>> _requests
            = new();

        private readonly ConcurrentDictionary<int, List<Candle>> _buffers
            = new();

        public async Task<List<Candle>> GetCandles(
            string ticker,
            Timeframe timeframe,
            DateTime endTimeUtc,
            int bars)
        {
            var contract = TwsContractFactory.CreateStock(ticker);

            var reqId = Interlocked.Increment(ref _nextRequestId);

            var tcs = new TaskCompletionSource<List<Candle>>();

            _requests[reqId] = tcs;
            _buffers[reqId] = new List<Candle>();

            _client.reqHistoricalData(
                reqId,
                contract,
                endTimeUtc.ToIbEndTime(),
                timeframe.ToIBDuration(bars),
                timeframe.ToIBBarSize(),
                "TRADES",
                0,
                1,
                false,
                null);

            return await tcs.Task;
        }
    }
}
