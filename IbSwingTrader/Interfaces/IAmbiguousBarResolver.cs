using IBApi;
using IbSwingTrader.Models;
using IbSwingTrader.Models.Stocks;

namespace IbSwingTrader.Interfaces
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
