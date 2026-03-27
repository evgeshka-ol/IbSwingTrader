using IBApi;

namespace IbSwingTrader.Abstractions.Market
{
    public interface ITwsMarketScheduleProvider
    {
        Task<MarketSessionSchedule?> TryGetScheduleAsync(
            Contract contract,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken = default);
    }
}
