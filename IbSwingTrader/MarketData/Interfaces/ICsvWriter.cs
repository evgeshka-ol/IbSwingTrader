namespace IbSwingTrader.MarketData.Interfaces
{
    public interface ICsvWriter
    {
        void Write<T>(string path, IEnumerable<T> rows);
    }
}
