namespace IbSwingTrader.Domain.Market
{
    public class HistoricalDataRequest
    {
        public required string Ticker { get; set; }

        public Timeframe Timeframe { get; set; }

        public DateTime EndTimeUtc { get; set; }

        public int Bars { get; set; }
    }
}
