namespace IbSwingTrader.Domain.Trading
{
    public class TradePlan
    {
        public decimal Entry { get; set; }
        public decimal Exit { get; set; }
        public decimal Stop { get; set; }
        public string? ExitProfile { get; set; }
    }
}
