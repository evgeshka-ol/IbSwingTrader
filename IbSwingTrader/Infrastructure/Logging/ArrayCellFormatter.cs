using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class ArrayCellFormatter(
            INumberTextFormatter numberFormatter) : IArrayCellFormatter
    {
        private readonly INumberTextFormatter _fmt = numberFormatter;

        public string Format(decimal first, decimal second, decimal third)
        {
            return $"[{_fmt.Generic(first)} {_fmt.Generic(second)} {_fmt.Generic(third)}]";
        }

        public string? Format(decimal? first, decimal? second, decimal? third)
        {
            if (!first.HasValue || !second.HasValue || !third.HasValue)
                return null;

            return $"[{_fmt.Generic(first.Value)} {_fmt.Generic(second.Value)} {_fmt.Generic(third.Value)}]";
        }
    }
}
