using IbSwingTrader.Models.Settings;

namespace IbSwingTrader.Interfaces
{
    public interface IMarketSessionSettingsProvider
    {
        MarketSessionsSettings Get();
    }
}