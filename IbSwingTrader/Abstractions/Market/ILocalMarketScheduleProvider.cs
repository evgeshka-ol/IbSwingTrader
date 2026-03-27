
namespace IbSwingTrader.Abstractions.Market
{
    public interface ILocalMarketScheduleProvider
    {
        MarketSessionSchedule BuildSchedule(DateTime startUtc, DateTime endUtc);
    }
}
