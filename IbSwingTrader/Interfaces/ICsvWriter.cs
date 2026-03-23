namespace IbSwingTrader.Interfaces
{
    public interface ICsvWriter
    {
        void Write<T>(string path, IReadOnlyCollection<T> rows);
    }
}
