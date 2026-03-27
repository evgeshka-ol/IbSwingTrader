using IBApi;

namespace IbSwingTrader.Abstractions.Market
{
    public interface IAmbiguousBarResolver
    {
        Task<AmbiguousBarResolutionResult?> ResolveLongAsync(
            CandidateDetails candidate,
            Contract contract,
            Candle parentCandle,
            decimal entryPrice,
            decimal exitPrice,
            decimal stopLoss);
    }
}
