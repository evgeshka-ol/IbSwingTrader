namespace IbSwingTrader.Abstractions.Evaluation
{
    public interface IFileHashService
    {
        Task<string> ComputeSha256Async(string path);
    }
}
