using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IFutureStatsCalculator
    {
        void Calculate(
            TradeDatasetRow row,
            List<Candle> candles,
            int entryIndex);
    }
}
