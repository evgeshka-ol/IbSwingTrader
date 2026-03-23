using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class ArrayCellFormatter(
        INumberTextFormatter numberFormatter) : IArrayCellFormatter
    {
        private readonly INumberTextFormatter _fmt = numberFormatter;

        public string Format(IEnumerable<decimal> values)
        {
            var parts = values.Select(_fmt.Generic);
            return $"[{string.Join(" ", parts)}]";
        }
    }
}