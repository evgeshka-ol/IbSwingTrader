
using IBApi;

namespace IbSwingTrader.Abstractions.Market
{
    public interface IContractResolver
    {
        Task<Contract> ResolveStockAsync(string ticker, TimeSpan? timeout = null, int maxAttempts = 3);
    }
}
