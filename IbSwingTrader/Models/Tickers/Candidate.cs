namespace IbSwingTrader.Models.Tickers
{
    public class Candidate : TickerEntity
    {
        public required TradePlanInfo TradePlan { get; set; }
    }
}