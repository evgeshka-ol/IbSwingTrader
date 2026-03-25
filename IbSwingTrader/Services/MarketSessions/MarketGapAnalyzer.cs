using IBApi;
using IbSwingTrader.Extensions;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.MarketSessions
{
    public class MarketGapAnalyzer(
        IMarketScheduleResolver marketScheduleResolver,
        IMarketSessionSettingsProvider marketSessionSettingsProvider,
        IHistoricalCache historicalCache) : IMarketGapAnalyzer
    {
        private const int MaxSearchDays = 10;
        private const int ObservedLookbackDays = 15;
        private const double ObservedSlotMinRatio = 0.50;

        private readonly IMarketScheduleResolver _marketScheduleResolver = marketScheduleResolver;
        private readonly IMarketSessionSettingsProvider _marketSessionSettingsProvider = marketSessionSettingsProvider;
        private readonly IHistoricalCache _historicalCache = historicalCache;

        public async Task<bool> IsExpectedGapAsync(
            Contract contract,
            Timeframe timeframe,
            DateTime previousBarUtc,
            DateTime currentBarUtc,
            CancellationToken cancellationToken = default)
        {
            if (currentBarUtc <= previousBarUtc)
                return true;

            var nextExpected = await GetNextExpectedBarTimeAsync(
                contract,
                timeframe,
                previousBarUtc,
                cancellationToken);

            if (!nextExpected.HasValue)
                return true;

            var tolerance = GetSlotTolerance(timeframe);

            return currentBarUtc <= nextExpected.Value.Add(tolerance);
        }

        public async Task<DateTime?> GetNextExpectedBarTimeAsync(
            Contract contract,
            Timeframe timeframe,
            DateTime previousBarUtc,
            CancellationToken cancellationToken = default)
        {
            if (!IsIntraday(timeframe))
                return previousBarUtc + timeframe.ToTimeSpan();

            var settings = _marketSessionSettingsProvider.Get();

            var scheduleStartUtc = previousBarUtc.AddDays(-ObservedLookbackDays);
            var scheduleEndUtc = previousBarUtc.AddDays(MaxSearchDays);

            var schedule = await _marketScheduleResolver.GetScheduleAsync(
                contract,
                scheduleStartUtc,
                scheduleEndUtc,
                cancellationToken);

            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);

            var observedSlots = TryBuildObservedSlotPattern(
                contract,
                timeframe,
                previousBarUtc,
                timeZone,
                settings.UseExtendedHoursByDefault);

            if (observedSlots is { Count: > 0 })
            {
                var observedNext = FindNextObservedSlot(
                    schedule,
                    timeZone,
                    previousBarUtc,
                    observedSlots,
                    settings.UseExtendedHoursByDefault);

                if (observedNext.HasValue)
                    return observedNext;
            }

            return FindNextFallbackSlot(
                schedule,
                timeZone,
                timeframe,
                previousBarUtc,
                settings.UseExtendedHoursByDefault);
        }

        private IReadOnlyList<TimeOnly>? TryBuildObservedSlotPattern(
            Contract contract,
            Timeframe timeframe,
            DateTime previousBarUtc,
            TimeZoneInfo timeZone,
            bool useExtendedHours)
        {
            var symbol = contract.Symbol;
            if (string.IsNullOrWhiteSpace(symbol))
                return null;

            if (!_historicalCache.TryLoad(symbol, timeframe, out var cached) ||
                cached is null ||
                cached.Count == 0)
            {
                return null;
            }

            var cutoffUtc = previousBarUtc.AddDays(-ObservedLookbackDays);

            var recentCandles = cached
                .Where(x => x.Time < previousBarUtc && x.Time >= cutoffUtc)
                .OrderBy(x => x.Time)
                .ToList();

            if (recentCandles.Count == 0)
                return null;

            var groupedByLocalDay = recentCandles
                .GroupBy(x => TimeZoneInfo.ConvertTimeFromUtc(x.Time, timeZone).Date)
                .OrderByDescending(x => x.Key)
                .Take(ObservedLookbackDays)
                .ToList();

            if (groupedByLocalDay.Count < 3)
                return null;

            var slotCounts = new Dictionary<TimeOnly, int>();
            var effectiveDays = 0;

            foreach (var dayGroup in groupedByLocalDay)
            {
                var localSlots = dayGroup
                    .Select(x => TimeZoneInfo.ConvertTimeFromUtc(x.Time, timeZone))
                    .Select(TimeOnly.FromDateTime)
                    .Where(x => IsAllowedLocalTime(x, useExtendedHours))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                if (localSlots.Count == 0)
                    continue;

                effectiveDays++;

                foreach (var slot in localSlots)
                {
                    if (!slotCounts.TryAdd(slot, 1))
                        slotCounts[slot]++;
                }
            }

            if (effectiveDays < 3 || slotCounts.Count == 0)
                return null;

            var minRequired = Math.Max(2, (int)Math.Ceiling(effectiveDays * ObservedSlotMinRatio));

            var observed = slotCounts
                .Where(x => x.Value >= minRequired)
                .Select(x => x.Key)
                .OrderBy(x => x)
                .ToList();

            return observed.Count == 0 ? null : observed;
        }

        private static DateTime? FindNextObservedSlot(
            MarketSessionSchedule schedule,
            TimeZoneInfo timeZone,
            DateTime previousBarUtc,
            IReadOnlyList<TimeOnly> observedSlots,
            bool useExtendedHours)
        {
            var previousLocal = TimeZoneInfo.ConvertTimeFromUtc(previousBarUtc, timeZone);
            var previousDate = DateOnly.FromDateTime(previousLocal.Date);

            foreach (var day in schedule.Days
                         .Where(x => x.IsTradingDay && x.Date >= previousDate)
                         .OrderBy(x => x.Date))
            {
                foreach (var slot in observedSlots)
                {
                    var localCandidate = day.Date.ToDateTime(slot);
                    var utcCandidate = TimeZoneInfo.ConvertTimeToUtc(localCandidate, timeZone);

                    if (utcCandidate <= previousBarUtc)
                        continue;

                    if (IsWithinAllowedSessions(day, utcCandidate, useExtendedHours))
                        return utcCandidate;
                }
            }

            return null;
        }

        private static DateTime? FindNextFallbackSlot(
            MarketSessionSchedule schedule,
            TimeZoneInfo timeZone,
            Timeframe timeframe,
            DateTime previousBarUtc,
            bool useExtendedHours)
        {
            var step = timeframe.ToTimeSpan();
            var previousLocal = TimeZoneInfo.ConvertTimeFromUtc(previousBarUtc, timeZone);
            var previousDate = DateOnly.FromDateTime(previousLocal.Date);

            foreach (var day in schedule.Days
                         .Where(x => x.IsTradingDay && x.Date >= previousDate)
                         .OrderBy(x => x.Date))
            {
                foreach (var session in day.Sessions
                             .Where(x => IsSessionAllowed(x, useExtendedHours))
                             .OrderBy(x => x.StartUtc))
                {
                    var sessionStartLocal = TimeZoneInfo.ConvertTimeFromUtc(session.StartUtc, timeZone);
                    var sessionEndLocal = TimeZoneInfo.ConvertTimeFromUtc(session.EndUtc, timeZone);

                    var localCandidate = sessionStartLocal;

                    while (localCandidate + step <= sessionEndLocal)
                    {
                        var utcCandidate = TimeZoneInfo.ConvertTimeToUtc(localCandidate, timeZone);

                        if (utcCandidate > previousBarUtc)
                            return utcCandidate;

                        localCandidate = localCandidate.Add(step);
                    }
                }
            }

            return null;
        }

        private static bool IsWithinAllowedSessions(
            TradingDaySchedule day,
            DateTime candidateUtc,
            bool useExtendedHours)
        {
            return day.Sessions.Any(x =>
                IsSessionAllowed(x, useExtendedHours) &&
                candidateUtc >= x.StartUtc &&
                candidateUtc < x.EndUtc);
        }

        private static bool IsAllowedLocalTime(TimeOnly time, bool useExtendedHours)
        {
            var preMarketStart = new TimeOnly(4, 0);
            var regularStart = new TimeOnly(9, 30);
            var regularEnd = new TimeOnly(16, 0);
            var afterHoursEnd = new TimeOnly(20, 0);

            if (time >= regularStart && time < regularEnd)
                return true;

            if (!useExtendedHours)
                return false;

            return time >= preMarketStart && time < afterHoursEnd;
        }

        private static bool IsSessionAllowed(SessionInterval session, bool useExtendedHours)
        {
            if (session.Type == MarketSessionType.Regular)
                return true;

            return useExtendedHours;
        }

        private static TimeSpan GetSlotTolerance(Timeframe timeframe)
        {
            return timeframe switch
            {
                Timeframe.M1 => TimeSpan.FromSeconds(30),
                Timeframe.M5 => TimeSpan.FromMinutes(1),
                Timeframe.M15 => TimeSpan.FromMinutes(2),
                Timeframe.M30 => TimeSpan.FromMinutes(3),
                Timeframe.H1 => TimeSpan.FromMinutes(5),
                Timeframe.H4 => TimeSpan.FromMinutes(10),
                Timeframe.D1 => TimeSpan.FromHours(1),
                Timeframe.W1 => TimeSpan.FromHours(6),
                _ => TimeSpan.FromMinutes(5)
            };
        }

        private static bool IsIntraday(Timeframe timeframe)
        {
            return timeframe == Timeframe.H4 ||
                   timeframe == Timeframe.H1 ||
                   timeframe == Timeframe.M30 ||
                   timeframe == Timeframe.M15 ||
                   timeframe == Timeframe.M5 ||
                   timeframe == Timeframe.M1;
        }
    }
}