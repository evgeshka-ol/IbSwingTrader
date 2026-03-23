namespace IbSwingTrader.Interfaces
{
    public interface IArrayCellFormatter
    {
        string Format(decimal first, decimal second, decimal third);
        string? Format(decimal? first, decimal? second, decimal? third);
    }
}
