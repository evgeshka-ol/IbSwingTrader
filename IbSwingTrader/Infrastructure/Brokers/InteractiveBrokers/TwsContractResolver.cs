using System.Collections.Concurrent;
using IBApi;

namespace IbSwingTrader.Infrastructure.Brokers.InteractiveBrokers
{
    public class TwsContractResolver(
        ITwsConnection tws,
        IAgentPathService pathService,
        IJsonFileService jsonFileService,
        ITextLogger logger) : IContractResolver
    {
        private readonly ITwsConnection _tws = tws;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly ITextLogger _logger = logger;
        private readonly ConcurrentDictionary<string, Contract> _cache = new();
        private readonly SemaphoreSlim _cacheLoadGate = new(1, 1);
        private readonly SemaphoreSlim _cacheSaveGate = new(1, 1);
        private readonly string _cacheFilePath = Path.Combine(pathService.GetCacheFolder(), "contract-resolver-cache.json");
        private volatile bool _cacheLoaded;

        public async Task<Contract> ResolveStockAsync(string ticker, TimeSpan? timeout = null, int maxAttempts = 3)
        {
            ticker = ticker.Trim().ToUpperInvariant();

            await EnsureCacheLoadedAsync();

            if (_cache.TryGetValue(ticker, out var cached))
            {
                _logger.Info($"ResolveStock cache hit: ticker={ticker}");
                return cached;
            }

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

            var attemptsToUse = Math.Clamp(maxAttempts, 1, attempts.Length);

            for (var i = 0; i < attemptsToUse; i++)
            {
                var request = attempts[i];
                _logger.Info(
                    $"ResolveStock attempt {i + 1}/{attemptsToUse}: ticker={ticker}, " +
                    $"exchange={request.Exchange}, primary={request.PrimaryExch}");

                var details = await _tws.GetContractDetails(request, timeout);
                var contract = details.FirstOrDefault()?.Contract;

                _logger.Info(
                    $"ResolveStock result {i + 1}/{attemptsToUse}: ticker={ticker}, " +
                    $"matches={details.Count}, exchange={request.Exchange}, primary={request.PrimaryExch}");

                if (contract != null)
                {
                    _logger.Info($"Contract resolved for {ticker} via {request.Exchange}/{request.PrimaryExch}");

                    if (_cache.TryAdd(ticker, contract))
                        await SaveCacheAsync();

                    return contract;
                }
            }

            throw new Exception($"Contract not found for {ticker}");
        }

        private async Task EnsureCacheLoadedAsync()
        {
            if (_cacheLoaded)
                return;

            await _cacheLoadGate.WaitAsync();
            try
            {
                if (_cacheLoaded)
                    return;

                try
                {
                    var entries = await _jsonFileService.ReadAsync<List<ContractCacheEntry>>(_cacheFilePath);
                    if (entries != null)
                    {
                        foreach (var entry in entries)
                        {
                            var ticker = entry.Ticker.Trim().ToUpperInvariant();
                            if (string.IsNullOrWhiteSpace(ticker))
                                continue;

                            _cache.TryAdd(ticker, entry.ToContract());
                        }

                        _logger.Info(
                            $"Contract cache loaded: path={_cacheFilePath}, entries={entries.Count}, cachedTickers={_cache.Count}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Info($"Contract cache load failed: {_cacheFilePath}. {ex.Message}");
                }

                _cacheLoaded = true;
            }
            finally
            {
                _cacheLoadGate.Release();
            }
        }

        private async Task SaveCacheAsync()
        {
            if (_cache.IsEmpty)
                return;

            await _cacheSaveGate.WaitAsync();
            try
            {
                var snapshot = _cache
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => ContractCacheEntry.FromContract(x.Key, x.Value))
                    .ToList();

                await _jsonFileService.WriteAsync(_cacheFilePath, snapshot);
            }
            catch (Exception ex)
            {
                _logger.Info($"Contract cache save failed: {_cacheFilePath}. {ex.Message}");
            }
            finally
            {
                _cacheSaveGate.Release();
            }
        }

        internal sealed class ContractCacheEntry
        {
            public string Ticker { get; set; } = string.Empty;
            public string Symbol { get; set; } = string.Empty;
            public string SecType { get; set; } = string.Empty;
            public string Exchange { get; set; } = string.Empty;
            public string PrimaryExch { get; set; } = string.Empty;
            public string Currency { get; set; } = string.Empty;
            public string TradingClass { get; set; } = string.Empty;
            public string LocalSymbol { get; set; } = string.Empty;
            public int ConId { get; set; }
            public string Multiplier { get; set; } = string.Empty;

            public static ContractCacheEntry FromContract(string ticker, Contract contract)
            {
                return new ContractCacheEntry
                {
                    Ticker = ticker,
                    Symbol = contract.Symbol ?? string.Empty,
                    SecType = contract.SecType ?? string.Empty,
                    Exchange = contract.Exchange ?? string.Empty,
                    PrimaryExch = contract.PrimaryExch ?? string.Empty,
                    Currency = contract.Currency ?? string.Empty,
                    TradingClass = contract.TradingClass ?? string.Empty,
                    LocalSymbol = contract.LocalSymbol ?? string.Empty,
                    ConId = contract.ConId,
                    Multiplier = contract.Multiplier ?? string.Empty
                };
            }

            public Contract ToContract()
            {
                return new Contract
                {
                    Symbol = Symbol,
                    SecType = SecType,
                    Exchange = Exchange,
                    PrimaryExch = PrimaryExch,
                    Currency = Currency,
                    TradingClass = TradingClass,
                    LocalSymbol = LocalSymbol,
                    ConId = ConId,
                    Multiplier = Multiplier
                };
            }
        }
    }
}
