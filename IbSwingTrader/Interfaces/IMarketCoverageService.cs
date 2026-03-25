using IBApi;
using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IMarketCoverageService
    {
        Task<bool> HasExpectedBarsBetweenAsync(
            Contract contract,
            Timeframe timeframe,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default);
    }
}
