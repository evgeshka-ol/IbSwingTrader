using IBApi;

namespace IbSwingTrader.Infrastructure.Brokers.InteractiveBrokers
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