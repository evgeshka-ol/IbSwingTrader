namespace IbSwingTrader.Interfaces
{
    public interface ICsvWriter
    {
        void Write<T>(string path, IEnumerable<T> rows);
    }
}
