namespace IbSwingTrader.Abstractions.Market
{
    public interface IHistoricalRetryPolicy
    {
        Task<T?> ExecuteAsync<T>(Func<Task<T?>> action, int retries = 2);
    }
}