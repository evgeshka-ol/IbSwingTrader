using IbSwingTrader.Models;
using IbSwingTrader.Models.Tickers;

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
