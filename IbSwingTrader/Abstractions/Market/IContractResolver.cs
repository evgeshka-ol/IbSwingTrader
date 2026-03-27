
using IBApi;

namespace IbSwingTrader.Abstractions.Market
{
    public interface IContractResolver
    {
        Task<Contract> ResolveStockAsync(string ticker);
    }
}
