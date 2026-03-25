using IBApi;
using IbSwingTrader.Extensions;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.MarketSessions
{
    public class MarketGapAnalyzer(
        IMarketScheduleResolver marketScheduleResolver,
        IMarketSessionSettingsProvider marketSessionSettingsProvider) : IMarketGapAnalyzer
    {
        private readonly IMarketScheduleResolver _marketScheduleResolver = marketScheduleResolver;
        private readonly IMarketSessionSettingsProvider _marketSessionSettingsProvider = marketSessionSettingsProvider;

        public async Task<bool> IsExpectedGapAsync(
            Contract contract,
            Timeframe timeframe,
            DateTime previousBarUtc,
            DateTime currentBarUtc,
            CancellationToken cancellationToken = default)
        {
            if (currentBarUtc <= previousBarUtc)
            {
                return true;
            }

            var nextExpected = await GetNextExpectedBarTimeAsync(
                contract,
                timeframe,
                previousBarUtc,
                cancellationToken);

            if (!nextExpected.HasValue)
            {
                return true;
            }

            return currentBarUtc <= nextExpected.Value;
        }

        public async Task<DateTime?> GetNextExpectedBarTimeAsync(
            Contract contract,
            Timeframe timeframe,
            DateTime previousBarUtc,
            CancellationToken cancellationToken = default)
        {
            var step = timeframe.ToTimeSpan();
            var settings = _marketSessionSettingsProvider.Get();

            var searchStartUtc = previousBarUtc.AddDays(-1);
            var searchEndUtc = previousBarUtc.AddDays(10);

            var schedule = await _marketScheduleResolver.GetScheduleAsync(
                contract,
                searchStartUtc,
                searchEndUtc,
                cancellationToken);

            var candidate = previousBarUtc + step;

            for (var i = 0; i < 500; i++)
            {
                if (IsInsideAllowedSession(schedule, candidate, settings.UseExtendedHoursByDefault))
                {
                    return candidate;
                }

                var nextSession = FindNextAllowedSession(schedule, candidate, settings.UseExtendedHoursByDefault);
                if (nextSession is null)
                {
                    return null;
                }

                if (candidate < nextSession.StartUtc)
                {
                    candidate = nextSession.StartUtc;
                }
                else
                {
                    candidate = nextSession.EndUtc;
                }
            }

            return null;
        }

        private static bool IsInsideAllowedSession(MarketSessionSchedule schedule, DateTime utc, bool useExtendedHours)
        {
            return schedule.Days
                .SelectMany(x => x.Sessions)
                .Any(x => IsSessionAllowed(x, useExtendedHours) && utc >= x.StartUtc && utc < x.EndUtc);
        }

        private static SessionInterval? FindNextAllowedSession(MarketSessionSchedule schedule, DateTime utc, bool useExtendedHours)
        {
            return schedule.Days
                .SelectMany(x => x.Sessions)
                .Where(x => IsSessionAllowed(x, useExtendedHours) && x.EndUtc > utc)
                .OrderBy(x => x.StartUtc)
                .FirstOrDefault();
        }

        private static bool IsSessionAllowed(SessionInterval session, bool useExtendedHours)
        {
            if (session.Type == MarketSessionType.Regular)
            {
                return true;
            }

            return useExtendedHours;
        }
    }
}