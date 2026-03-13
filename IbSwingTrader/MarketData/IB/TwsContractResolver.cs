using System.Collections.Concurrent;
using IBApi;
using IbSwingTrader.Interfaces;

namespace IbSwingTrader.MarketData.IB
{
    public class TwsContractResolver(ITwsConnection tws, ILogger logger) : IContractResolver
    {
        private readonly ITwsConnection _tws = tws;
        private readonly ILogger _logger = logger;
        private readonly ConcurrentDictionary<string, Contract> _cache = new();

        public async Task<Contract> ResolveStockAsync(string ticker)
        {
            ticker = ticker.Trim().ToUpperInvariant();

            if (_cache.TryGetValue(ticker, out var cached))
                return cached;

            var attempts = new[]
            {
                new Contract
                {
                    Symbol = ticker,
                    SecType = "STK",
                    Exchange = "SMART",
                    Currency = "USD"
                },

                new Contract
                {
                    Symbol = ticker,
                    SecType = "STK",
                    Exchange = "SMART",
                    PrimaryExch = "NASDAQ",
                    Currency = "USD"
                },

                new Contract
                {
                    Symbol = ticker,
                    SecType = "STK",
                    Exchange = "NASDAQ",
                    Currency = "USD"
                }
            };

            foreach (var request in attempts)
            {
                var details = await _tws.GetContractDetails(request);

                var contract = details.FirstOrDefault()?.Contract;
                _logger.Debug($"Contract resolved for {ticker} via {request.Exchange}/{request.PrimaryExch}");

                if (contract != null)
                {
                    _cache[ticker] = contract;
                    return contract;
                }
            }

            throw new Exception($"Contract not found for {ticker}");
        }
    }
}