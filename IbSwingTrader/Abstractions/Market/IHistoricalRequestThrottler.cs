namespace IbSwingTrader.Abstractions.Market
{
    public interface IHistoricalRequestThrottler
    {
        Task<IDisposable> AcquireAsync();
    }
}
