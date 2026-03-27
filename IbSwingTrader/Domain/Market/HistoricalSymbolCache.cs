namespace IbSwingTrader.Domain.Market
{
    public class HistoricalSymbolCache
    {
        public string Symbol { get; set; } = string.Empty;

        public Dictionary<string, List<Candle>> Timeframes { get; set; } = new();
    }
}