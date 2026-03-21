using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Settings
{
    public class AgentPathService : IAgentPathService
    {
        private readonly PathSettings _paths;

        public AgentPathService(IAgentSettingsProvider settingsProvider)
        {
            _paths = settingsProvider.Get().Paths;
        }

        public string GetDataRoot() => _paths.DataRoot;
        public string GetCacheFolder() => Combine(_paths.CacheFolder);
        public string GetDatasetFolder() => Combine(_paths.DatasetFolder);
        public string GetCandidatesFolder() => Combine(_paths.CandidatesFolder);
        public string GetEvaluationsFolder() => Combine(_paths.EvaluationsFolder);
        public string GetWishListFile() => Combine(_paths.WishListFile);
        public string GetProcessedCandidateFilesManifest() => Combine(_paths.ProcessedCandidateFilesManifest);
        public string GetLogsFolder() => Combine(_paths.LogsFolder);

        private string Combine(string relativePath) =>
            Path.Combine(_paths.DataRoot, relativePath);
    }
}
