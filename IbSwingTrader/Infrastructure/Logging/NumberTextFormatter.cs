using System.Globalization;
using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class NumberTextFormatter : INumberTextFormatter
    {
        public string Price(decimal value) =>
            PriceValue(value).ToString("0.00", CultureInfo.InvariantCulture);

        public string Percent(decimal value) =>
            PercentValue(value).ToString("0.00", CultureInfo.InvariantCulture);

        public string Ratio(decimal value) =>
            RatioValue(value).ToString("0.###", CultureInfo.InvariantCulture);

        public string Hours(decimal value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        public string Generic(decimal value) =>
            GenericValue(value).ToString("0.##", CultureInfo.InvariantCulture);

        public decimal PriceValue(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        public decimal PercentValue(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        public decimal RatioValue(decimal value) =>
            Math.Round(value, 3, MidpointRounding.AwayFromZero);

        public decimal GenericValue(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}