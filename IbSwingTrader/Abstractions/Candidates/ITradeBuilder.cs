namespace IbSwingTrader.Abstractions.Candidates
{
    public interface ITradeBuilder
    {
        TradePlan Build(
            List<Candle> candles,
            List<Candle>? entryCandles = null,
            decimal? scanPriceOverride = null,
            decimal? entryDiscountOverridePct = null,
            decimal? defaultProfitPctOverride = null,
            decimal? minProfitPctOverride = null,
            decimal? maxProfitPctOverride = null,
            decimal? maxLossPctOverride = null);
    }
}
