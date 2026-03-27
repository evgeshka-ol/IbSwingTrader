using IBApi;

namespace IbSwingTrader.Abstractions.Market
{
    public interface IMarketScheduleCache
    {
        bool TryLoad(
            Contract contract,
            DateTime startUtc,
            DateTime endUtc,
            out MarketSessionSchedule? schedule);

        void Save(
            Contract contract,
            DateTime startUtc,
            DateTime endUtc,
            MarketSessionSchedule schedule);
    }
}
