namespace IbSwingTrader.Abstractions.Dataset
{
    public interface ITradePositionMerger
    {
        List<TradeRecord> Merge(List<TradeRecord> rawFills);
    }
}
