namespace IbSwingTrader.Interfaces
{
    public interface IJsonFileService
    {
        Task<T?> ReadAsync<T>(string path);
        Task WriteAsync<T>(string path, T data);
    }
}
