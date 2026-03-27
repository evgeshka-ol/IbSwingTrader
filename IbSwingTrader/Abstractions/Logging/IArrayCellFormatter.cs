namespace IbSwingTrader.Abstractions.Logging
{
    public interface IArrayCellFormatter
    {
        string Format(IEnumerable<decimal> values);
    }
}