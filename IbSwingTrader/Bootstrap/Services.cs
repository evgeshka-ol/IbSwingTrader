using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Bootstrap
{
    public class Services
    {
        public required ITradeDatasetBuilder DatasetBuilder { get; init; }
        public required ICsvWriter CsvWriter { get; init; }
    }
}
