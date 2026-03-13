using IbSwingTrader.Interfaces;
using IbSwingTrader.MarketData.IB;

namespace IbSwingTrader.Bootstrap
{
    public class Services
    {
        public required TwsConnection Connection { get; init; }
        public required ITradeDatasetBuilder DatasetBuilder { get; init; }
        public required ICsvWriter CsvWriter { get; init; }
        public required IContractResolver ContractResolver { get; init; }
    }
}
