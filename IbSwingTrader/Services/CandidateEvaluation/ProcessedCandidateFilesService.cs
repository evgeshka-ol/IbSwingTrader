using System.Text.Json;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateEvaluation
{
    public class ProcessedCandidateFilesService : IProcessedCandidateFilesService
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true
        };

        public async Task<ProcessedCandidateFilesManifest> ReadAsync(string path)
        {
            if (!File.Exists(path))
                return new ProcessedCandidateFilesManifest();

            var json = await File.ReadAllTextAsync(path);
            var manifest = JsonSerializer.Deserialize<ProcessedCandidateFilesManifest>(json, Options);

            return manifest ?? new ProcessedCandidateFilesManifest();
        }

        public async Task WriteAsync(string path, ProcessedCandidateFilesManifest manifest)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(manifest, Options);
            await File.WriteAllTextAsync(path, json);
        }

        public bool IsProcessed(
            ProcessedCandidateFilesManifest manifest,
            string sha256)
        {
            return manifest.Files.Any(x =>
                string.Equals(x.Sha256, sha256, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        }

        public void MarkProcessed(
            ProcessedCandidateFilesManifest manifest,
            ProcessedCandidateFile file)
        {
            manifest.Files.Add(file);
        }
    }
}
