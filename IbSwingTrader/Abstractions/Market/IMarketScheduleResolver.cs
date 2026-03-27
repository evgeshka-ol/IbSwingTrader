using IBApi;

namespace IbSwingTrader.Abstractions.Market
{
    public interface IMarketScheduleResolver
    {
        Task<MarketSessionSchedule> GetScheduleAsync(
            Contract contract,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken = default);
    }
}