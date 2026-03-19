using IbSwingTrader.Models;

namespace IbSwingTrader.Interfaces
{
    public interface IProcessedCandidateFilesService
    {
        Task<ProcessedCandidateFilesManifest> ReadAsync(string path);
        Task WriteAsync(string path, ProcessedCandidateFilesManifest manifest);

        bool IsProcessed(
            ProcessedCandidateFilesManifest manifest,
            string sha256);

        void MarkProcessed(
            ProcessedCandidateFilesManifest manifest,
            ProcessedCandidateFile file);
    }
}
