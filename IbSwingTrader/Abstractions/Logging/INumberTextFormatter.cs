namespace IbSwingTrader.Abstractions.Logging
{
    public interface INumberTextFormatter
    {
        string Price(decimal value);
        string Percent(decimal value);
        string Ratio(decimal value);
        string Hours(decimal value);
        string Generic(decimal value);

        decimal PriceValue(decimal value);
        decimal PercentValue(decimal value);
        decimal RatioValue(decimal value);
        decimal GenericValue(decimal value);
    }
}