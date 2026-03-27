namespace IbSwingTrader.Domain.Market
{
    public enum MarketSessionType
    {
        Unknown = 0,
        PreMarket = 1,
        Regular = 2,
        AfterHours = 3
    }

    public enum MarketSessionScope
    {
        Unknown = 0,
        TradingHours = 1,
        LiquidHours = 2
    }
}
