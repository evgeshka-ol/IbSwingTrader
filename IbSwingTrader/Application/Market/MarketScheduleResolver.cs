using IBApi;

namespace IbSwingTrader.Application.Market
{
    public class MarketScheduleResolver(
        IMarketSessionSettingsProvider marketSessionSettingsProvider,
        IMarketScheduleCache marketScheduleCache,
        ITwsMarketScheduleProvider twsMarketScheduleProvider,
        ILocalMarketScheduleProvider localMarketScheduleProvider) : IMarketScheduleResolver
    {
        private readonly IMarketSessionSettingsProvider _marketSessionSettingsProvider = marketSessionSettingsProvider;
        private readonly IMarketScheduleCache _marketScheduleCache = marketScheduleCache;
        private readonly ITwsMarketScheduleProvider _twsMarketScheduleProvider = twsMarketScheduleProvider;
        private readonly ILocalMarketScheduleProvider _localMarketScheduleProvider = localMarketScheduleProvider;

        public async Task<MarketSessionSchedule> GetScheduleAsync(
            Contract contract,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken = default)
        {
            if (_marketScheduleCache.TryLoad(contract, startUtc, endUtc, out var cached) && cached is not null)
            {
                return cached;
            }

            var settings = _marketSessionSettingsProvider.Get();

            if (settings.TwsOverrides.Enabled)
            {
                var fromTws = await _twsMarketScheduleProvider.TryGetScheduleAsync(
                    contract,
                    startUtc,
                    endUtc,
                    cancellationToken);

                if (fromTws is not null)
                {
                    _marketScheduleCache.Save(contract, startUtc, endUtc, fromTws);
                    return fromTws;
                }
            }

            var local = _localMarketScheduleProvider.BuildSchedule(startUtc, endUtc);
            _marketScheduleCache.Save(contract, startUtc, endUtc, local);

            return local;
        }
    }
}