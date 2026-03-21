namespace IbSwingTrader.Interfaces
{
    public interface IAgentPathService
    {
        string GetDataRoot();
        string GetCacheFolder();
        string GetDatasetFolder();
        string GetCandidatesFolder();
        string GetEvaluationsFolder();
        string GetWishListFile();
        string GetProcessedCandidateFilesManifest();
        string GetLogsFolder();
    }
}
