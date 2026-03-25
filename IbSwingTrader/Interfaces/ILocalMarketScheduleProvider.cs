using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ILocalMarketScheduleProvider
    {
        MarketSessionSchedule BuildSchedule(DateTime startUtc, DateTime endUtc);
    }
}
