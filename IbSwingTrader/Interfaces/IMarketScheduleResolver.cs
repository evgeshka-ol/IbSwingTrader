using IBApi;
using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
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