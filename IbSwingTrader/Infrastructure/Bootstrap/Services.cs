using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Bootstrap
{
    public class Services
    {
        public required ITextLogger Logger { get; init; }
        public required ITwsConnection Connection { get; init; }
        public required ITradeDatasetBuilder DatasetBuilder { get; init; }
        public required ICsvWriter CsvWriter { get; init; }
        public required IContractResolver ContractResolver { get; init; }
        public required IHistoricalDataService HistoricalService { get; init; }
        public required ICommand GetCandidatesCommand { get; init; }
        public required ICommand GetScannerParamsCommand { get; init; }
        public required ICommand EvaluateCandidatesFolderCommand { get; init; }
    }
}
