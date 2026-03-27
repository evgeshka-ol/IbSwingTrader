using System.Collections.Concurrent;
using IBApi;

namespace IbSwingTrader.Application.Market
{
    public class InMemoryMarketScheduleCache : IMarketScheduleCache
    {
        private readonly ConcurrentDictionary<string, MarketSessionSchedule> _cache = new();

        public bool TryLoad(
            Contract contract,
            DateTime startUtc,
            DateTime endUtc,
            out MarketSessionSchedule? schedule)
        {
            var key = BuildKey(contract, startUtc, endUtc);

            if (_cache.TryGetValue(key, out var cached))
            {
                schedule = cached;
                return true;
            }

            schedule = null;
            return false;
        }

        public void Save(
            Contract contract,
            DateTime startUtc,
            DateTime endUtc,
            MarketSessionSchedule schedule)
        {
            var key = BuildKey(contract, startUtc, endUtc);
            _cache[key] = schedule;
        }

        private static string BuildKey(Contract contract, DateTime startUtc, DateTime endUtc)
        {
            return string.Join("|",
                contract.Symbol ?? string.Empty,
                contract.SecType ?? string.Empty,
                contract.Exchange ?? string.Empty,
                contract.PrimaryExch ?? string.Empty,
                contract.Currency ?? string.Empty,
                contract.ConId,
                startUtc.ToUniversalTime().ToString("O"),
                endUtc.ToUniversalTime().ToString("O"));
        }
    }
}