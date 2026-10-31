namespace IbSwingTrader.Domain.Market
{
    // Zero-volume carry-forward bars describe a grid slot, not a traded price.
    public static class TradeObservation
    {
        public static bool HasValidOhlc(Candle bar) => bar.Open > 0m && bar.Close > 0m && bar.Low > 0m &&
            bar.High >= Math.Max(bar.Open, bar.Close) && bar.Low <= Math.Min(bar.Open, bar.Close);

        public static bool IsTrade(Candle bar) => bar.Volume > 0m && HasValidOhlc(bar);

        public static bool Touches(Candle bar, decimal price) => price > 0m && IsTrade(bar) &&
            bar.Low <= price && bar.High >= price;
    }
}
