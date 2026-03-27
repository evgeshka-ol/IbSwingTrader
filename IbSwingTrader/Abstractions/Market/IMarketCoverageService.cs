using IBApi;

namespace IbSwingTrader.Abstractions.Market
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
