using IBApi;

namespace IbSwingTrader.Abstractions.Market
{
    public interface IAmbiguousBarResolver
    {
        Task<List<Candle>?> ResolveEntryBarAsync(CandidateDetails candidate,
            Contract contract, Candle parentCandle, decimal entryPrice)
            => Task.FromResult<List<Candle>?>(null);
        Task<AmbiguousBarResolutionResult?> ResolveLongAsync(
            CandidateDetails candidate,
            Contract contract,
            Candle parentCandle,
            decimal entryPrice,
            decimal exitPrice,
            decimal stopLoss);
    }
}
