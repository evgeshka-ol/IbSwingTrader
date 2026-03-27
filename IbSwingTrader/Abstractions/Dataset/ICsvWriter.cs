namespace IbSwingTrader.Abstractions.Dataset
{
    public interface ICsvWriter
    {
        void Write<T>(string path, IEnumerable<T> rows);
    }
}
