using System.Collections.Concurrent;
using IBApi;
using IbSwingTrader.Interfaces;

namespace IbSwingTrader.MarketData.IB
{
    public class TwsContractResolver(TwsConnection tws) : IContractResolver
    {
        private readonly TwsConnection _tws = tws;

        private readonly ConcurrentDictionary<string, Contract> _cache = new();

        public async Task<Contract> ResolveStockAsync(string ticker)
        {
            ticker = ticker.Trim().ToUpperInvariant();

            if (_cache.TryGetValue(ticker, out var cached))
                return cached;

            var request = new Contract
            {
                Symbol = ticker,
                SecType = "STK",
                Exchange = "SMART",
                Currency = "USD"
            };

            var details = await _tws.GetContractDetails(request);

            var contract = (details.FirstOrDefault()?.Contract) ?? throw new Exception($"Contract not found for {ticker}");
            _cache[ticker] = contract;

            return contract;
        }
    }
}