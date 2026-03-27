namespace IbSwingTrader.Domain.Market
{
    public class Candle
    {
        public Timeframe Timeframe { get; set; }

        public DateTime Time { get; set; }

        public decimal Open { get; set; }

        public decimal High { get; set; }

        public decimal Low { get; set; }

        public decimal Close { get; set; }

        public decimal Volume { get; set; }
    }
}
