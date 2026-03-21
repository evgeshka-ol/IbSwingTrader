using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Bootstrap
{
    public class Services
    {
        public required ITextLogger Logger { get; init; }
        public required ICommand BuildDatasetCommand { get; init; }
        public required ICommand GetCandidatesCommand { get; init; }
        public required ICommand EvaluateCandidatesFolderCommand { get; init; }
        public required ICommand GetScannerParamsCommand { get; init; }
        public required ICommand DownloadFundamentalSnapshotCommand { get; init; }
    }
}
