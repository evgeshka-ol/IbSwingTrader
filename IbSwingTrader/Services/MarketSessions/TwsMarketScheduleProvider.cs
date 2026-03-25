using IBApi;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.MarketSessions
{
    public class TwsMarketScheduleProvider : ITwsMarketScheduleProvider
    {
        public Task<MarketSessionSchedule?> TryGetScheduleAsync(
            Contract contract,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<MarketSessionSchedule?>(null);
        }
    }
}