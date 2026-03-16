using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface ITradeBuilder
    {
        TradePlan Build(List<Candle> candles);
    }
}
