using IBApi;
using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
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
