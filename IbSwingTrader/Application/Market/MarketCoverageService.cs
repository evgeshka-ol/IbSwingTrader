using IBApi;

namespace IbSwingTrader.Application.Market
{
    public class MarketCoverageService(
        IMarketGapAnalyzer marketGapAnalyzer) : IMarketCoverageService
    {
        private readonly IMarketGapAnalyzer _marketGapAnalyzer = marketGapAnalyzer;

        public async Task<bool> HasExpectedBarsBetweenAsync(
            Contract contract,
            Timeframe timeframe,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default)
        {
            if (toUtc <= fromUtc)
            {
                return false;
            }

            var nextExpected = await _marketGapAnalyzer.GetNextExpectedBarTimeAsync(
                contract,
                timeframe,
                fromUtc,
                cancellationToken);

            return nextExpected.HasValue && nextExpected.Value < toUtc;
        }
    }
}