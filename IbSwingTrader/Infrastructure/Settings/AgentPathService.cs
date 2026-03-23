using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class AgentPathService : IAgentPathService
    {
        private readonly PathSettings _paths;
        private readonly string _dataRootFullPath;

        public AgentPathService(IAgentSettingsProvider settingsProvider)
        {
            _paths = settingsProvider.Get().Paths;

            _dataRootFullPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, _paths.DataRoot));
        }

        public string GetDataRoot() => _dataRootFullPath;
        public string GetTradesFile() => Combine(_paths.TradesFile);
        public string GetDatasetFile() => Combine(_paths.DatasetFile);
        public string GetCacheFolder() => Combine(_paths.CacheFolder);
        public string GetCandidatesFolder() => Combine(_paths.CandidatesFolder);
        public string GetEvaluationsFolder() => Combine(_paths.EvaluationsFolder);
        public string GetWishListFile() => Combine(_paths.WishListFile);

        public string GetProcessedCandidateFilesManifest() =>
            Combine(_paths.ProcessedCandidateFilesManifest);

        public string GetLogsFolder() => Combine(_paths.LogsFolder);

        private string Combine(string relativePath) =>
            Path.GetFullPath(Path.Combine(_dataRootFullPath, relativePath));
    }
}