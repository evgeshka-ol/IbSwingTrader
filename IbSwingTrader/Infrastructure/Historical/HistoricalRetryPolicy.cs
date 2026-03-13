using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Historical
{
    public class HistoricalRetryPolicy : IHistoricalRetryPolicy
    {
        public async Task<T?> ExecuteAsync<T>(
            Func<Task<T?>> action,
            int retries = 2)
        {
            for (int i = 0; i <= retries; i++)
            {
                var result = await action();

                if (result != null)
                    return result;

                await Task.Delay(2000);
            }

            return default;
        }
    }
}