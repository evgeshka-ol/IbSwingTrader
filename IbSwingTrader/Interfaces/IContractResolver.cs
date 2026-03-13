
using IBApi;

namespace IbSwingTrader.Interfaces
{
    public interface IContractResolver
    {
        Task<Contract> ResolveStockAsync(string ticker);
    }
}
