namespace IbSwingTrader.Interfaces
{
    public interface IHistoricalRequestThrottler
    {
        Task<IDisposable> AcquireAsync();
    }
}
