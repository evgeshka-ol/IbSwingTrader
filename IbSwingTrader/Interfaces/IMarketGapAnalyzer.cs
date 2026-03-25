using IBApi;
using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IMarketGapAnalyzer
    {
        Task<bool> IsExpectedGapAsync(
            Contract contract,
            Timeframe timeframe,
            DateTime previousBarUtc,
            DateTime currentBarUtc,
            CancellationToken cancellationToken = default);

        Task<DateTime?> GetNextExpectedBarTimeAsync(
            Contract contract,
            Timeframe timeframe,
            DateTime previousBarUtc,
            CancellationToken cancellationToken = default);
    }
}
