namespace IbSwingTrader.Abstractions.Settings
{
    public interface IAgentPathService
    {
        string GetDataRoot();
        string GetTradesFile();
        string GetDatasetFile();
        string GetCacheFolder();
        string GetCandidatesFolder();
        string GetEvaluationsFolder();
        string GetWishListFile();
        string GetProcessedCandidateFilesManifest();
        string GetLogsFolder();
    }
}
