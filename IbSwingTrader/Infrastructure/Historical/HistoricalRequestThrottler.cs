using IbSwingTrader.Interfaces;

namespace IbSwingTrader.Infrastructure.Historical
{
    public class HistoricalRequestThrottler(
        int maxParallel = 3,
        int delayMs = 250) : IHistoricalRequestThrottler
    {
        private readonly SemaphoreSlim _semaphore = new(maxParallel);
        private readonly TimeSpan _delay = TimeSpan.FromMilliseconds(delayMs);

        public async Task<IDisposable> AcquireAsync()
        {
            await _semaphore.WaitAsync();
            await Task.Delay(_delay);

            return new Releaser(_semaphore);
        }

        private class Releaser(SemaphoreSlim semaphore) : IDisposable
        {
            private readonly SemaphoreSlim _semaphore = semaphore;

            public void Dispose()
            {
                _semaphore.Release();
            }
        }
    }
}