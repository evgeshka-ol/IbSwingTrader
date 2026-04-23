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
        string GetEvaluationsArchiveFile();
        string GetWishListFile();
        string GetWishListEvaluationsFile();
        string GetProcessedCandidateFilesManifest();
        string GetLogsFolder();
    }
}
