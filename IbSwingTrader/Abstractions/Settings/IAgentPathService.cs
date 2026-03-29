namespace IbSwingTrader.Abstractions.Settings
{
    public interface IAgentPathService
    {
        string GetDataRoot();
        string GetTradesFile();
        string GetDatasetFile();
        string GetCacheFolder();
        string GetCandidatesFile();
        string GetEvaluationsFile();
        string GetWishListFile();
        string GetProcessedCandidateFilesManifest();
        string GetLogsFolder();
    }
}
