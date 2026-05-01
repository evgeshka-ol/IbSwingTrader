using System.Collections.Concurrent;
using IBApi;

namespace IbSwingTrader.Infrastructure.Brokers.InteractiveBrokers
{
    public class TwsContractResolver(ITwsConnection tws, ITextLogger logger) : IContractResolver
    {
        private readonly ITwsConnection _tws = tws;
        private readonly ITextLogger _logger = logger;
        private readonly ConcurrentDictionary<string, Contract> _cache = new();

        public async Task<Contract> ResolveStockAsync(string ticker, TimeSpan? timeout = null)
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

            for (var i = 0; i < attempts.Length; i++)
            {
                var request = attempts[i];
                _logger.Info(
                    $"ResolveStock attempt {i + 1}/{attempts.Length}: ticker={ticker}, " +
                    $"exchange={request.Exchange}, primary={request.PrimaryExch}");

                var details = await _tws.GetContractDetails(request, timeout);

                var contract = details.FirstOrDefault()?.Contract;
                _logger.Info(
                    $"ResolveStock result {i + 1}/{attempts.Length}: ticker={ticker}, " +
                    $"matches={details.Count}, exchange={request.Exchange}, primary={request.PrimaryExch}");

                if (contract != null)
                {
                    _logger.Info($"Contract resolved for {ticker} via {request.Exchange}/{request.PrimaryExch}");
                    _cache[ticker] = contract;
                    return contract;
                }
            }

            throw new Exception($"Contract not found for {ticker}");
        }
    }
}
