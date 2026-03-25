using IBApi;
using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
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
