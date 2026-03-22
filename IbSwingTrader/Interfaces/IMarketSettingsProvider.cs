using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IMarketSettingsProvider
    {
        MarketSettings Get();
    }
}