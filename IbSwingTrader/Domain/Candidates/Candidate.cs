namespace IbSwingTrader.Domain.Candidates
{
    public class Candidate : TickerEntity
    {
        public required TradePlanInfo TradePlan { get; set; }
    }
}