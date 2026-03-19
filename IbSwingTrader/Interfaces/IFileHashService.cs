namespace IbSwingTrader.Interfaces
{
    public interface IFileHashService
    {
        Task<string> ComputeSha256Async(string path);
    }
}
