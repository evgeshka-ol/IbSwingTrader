using IBApi;

namespace IbSwingTrader.Abstractions.Market
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
