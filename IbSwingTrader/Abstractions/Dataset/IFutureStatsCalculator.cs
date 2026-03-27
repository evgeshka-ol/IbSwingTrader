
namespace IbSwingTrader.Abstractions.Dataset
{
    public interface IFutureStatsCalculator
    {
        void Calculate(
            TradeDatasetRow row,
            List<Candle> candles,
            int entryIndex);
    }
}
