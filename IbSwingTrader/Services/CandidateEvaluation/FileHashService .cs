using System.Security.Cryptography;
using System.Text;
using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Services.CandidateEvaluation
{
    public class FileHashService : IFileHashService
    {
        public async Task<string> ComputeSha256Async(string path)
        {
            await using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();

            var hash = await sha.ComputeHashAsync(stream);
            var sb = new StringBuilder(hash.Length * 2);

            foreach (var b in hash)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }
    }
}
