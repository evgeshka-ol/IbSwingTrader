
namespace IbSwingTrader.Infrastructure.Historical
{
    public class HistoricalRequestThrottler : IHistoricalRequestThrottler
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly TimeSpan _delay;

        public HistoricalRequestThrottler(
            ITwsSettingsProvider settingsProvider)
        {
            var settings = settingsProvider.Get();

            _semaphore = new SemaphoreSlim(settings.MaxParallelHistoryRequests);
            _delay = TimeSpan.FromMilliseconds(settings.HistoryTimeoutSeconds);
        }

        public async Task<IDisposable> AcquireAsync()
        {
            await _semaphore.WaitAsync();
            await Task.Delay(_delay);

            return new Releaser(_semaphore);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;

            public Releaser(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                _semaphore.Release();
            }
        }
    }
}