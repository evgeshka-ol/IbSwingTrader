using IBApi;

namespace IbSwingTrader.Application.Market
{
    public class MarketGapAnalyzer(
        IMarketScheduleResolver marketScheduleResolver,
        IMarketSessionSettingsProvider marketSessionSettingsProvider,
        IHistoricalCache historicalCache) : IMarketGapAnalyzer
    {
        private const int MaxSearchDays = 10;
        private const int ObservedLookbackDays = 20;
        private const double ObservedSlotMinRatio = 0.45;

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

            var timeZone = await GetTimeZoneAsync(contract, previousBarUtc, cancellationToken);
            var previousLocal = TimeZoneInfo.ConvertTimeFromUtc(previousBarUtc, timeZone);
            var currentLocal = TimeZoneInfo.ConvertTimeFromUtc(currentBarUtc, timeZone);

            if (!IsIntraday(timeframe))
            {
                if (timeframe == Timeframe.D1)
                {
                    if (IsExpectedWeekendTransition(previousLocal, currentLocal))
                        return true;
                }

                var nextExpectedNonIntraday = previousBarUtc + timeframe.ToTimeSpan();
                return currentBarUtc <= nextExpectedNonIntraday.Add(GetSlotTolerance(timeframe));
            }

            if (IsExpectedWeekendTransition(previousLocal, currentLocal))
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
            var previousLocal = TimeZoneInfo.ConvertTimeFromUtc(previousBarUtc, timeZone);
            var previousDate = DateOnly.FromDateTime(previousLocal.Date);

            var observedPattern = TryBuildObservedPattern(
                contract,
                timeframe,
                previousBarUtc,
                timeZone,
                settings.UseExtendedHoursByDefault);

            if (observedPattern is not null)
            {
                var observedNext = FindNextObservedSlot(
                    schedule,
                    timeZone,
                    previousBarUtc,
                    previousLocal,
                    observedPattern,
                    settings.UseExtendedHoursByDefault);

                if (observedNext.HasValue)
                    return observedNext;
            }

            return FindNextFallbackSlot(
                schedule,
                timeZone,
                timeframe,
                previousBarUtc,
                previousDate,
                settings.UseExtendedHoursByDefault);
        }

        private async Task<TimeZoneInfo> GetTimeZoneAsync(
            Contract contract,
            DateTime referenceUtc,
            CancellationToken cancellationToken)
        {
            var schedule = await _marketScheduleResolver.GetScheduleAsync(
                contract,
                referenceUtc.AddDays(-1),
                referenceUtc.AddDays(1),
                cancellationToken);

            return TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        }

        private ObservedIntradayPattern? TryBuildObservedPattern(
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
                .GroupBy(x => x.Time.Date)
                .OrderByDescending(x => x.Key)
                .Take(ObservedLookbackDays)
                .ToList();

            if (groupedByLocalDay.Count < 3)
                return null;

            var slotCounts = new Dictionary<TimeOnly, int>();
            var transitionCounts = new Dictionary<TimeOnly, Dictionary<TimeOnly, int>>();
            var firstSlotCounts = new Dictionary<TimeOnly, int>();
            var effectiveDays = 0;

            foreach (var dayGroup in groupedByLocalDay)
            {
                var localSlots = dayGroup
                    .Select(x => TimeOnly.FromDateTime(x.Time))
                    .Where(x => IsAllowedLocalTime(x, useExtendedHours))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                if (localSlots.Count == 0)
                    continue;

                effectiveDays++;

                var firstSlot = localSlots[0];
                if (!firstSlotCounts.TryAdd(firstSlot, 1))
                    firstSlotCounts[firstSlot]++;

                foreach (var slot in localSlots)
                {
                    if (!slotCounts.TryAdd(slot, 1))
                        slotCounts[slot]++;
                }

                for (int i = 0; i < localSlots.Count - 1; i++)
                {
                    var from = localSlots[i];
                    var to = localSlots[i + 1];

                    if (!transitionCounts.TryGetValue(from, out var nextMap))
                    {
                        nextMap = new Dictionary<TimeOnly, int>();
                        transitionCounts[from] = nextMap;
                    }

                    if (!nextMap.TryAdd(to, 1))
                        nextMap[to]++;
                }
            }

            if (effectiveDays < 3 || slotCounts.Count == 0)
                return null;

            var minSlotRequired = Math.Max(2, (int)Math.Ceiling(effectiveDays * ObservedSlotMinRatio));

            var allowedSlots = slotCounts
                .Where(x => x.Value >= minSlotRequired)
                .Select(x => x.Key)
                .OrderBy(x => x)
                .ToList();

            if (allowedSlots.Count == 0)
                return null;

            var nextSlotBySlot = new Dictionary<TimeOnly, TimeOnly>();

            foreach (var pair in transitionCounts)
            {
                var from = pair.Key;
                var nextMap = pair.Value;

                var best = nextMap
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key)
                    .FirstOrDefault();

                if (best.Value >= 2)
                {
                    nextSlotBySlot[from] = best.Key;
                }
            }

            var commonFirstSlot = firstSlotCounts
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .FirstOrDefault();

            var openingSlot = commonFirstSlot.Value >= 2
                ? commonFirstSlot.Key
                : allowedSlots[0];

            return new ObservedIntradayPattern(
                allowedSlots,
                nextSlotBySlot,
                openingSlot);
        }

        private static DateTime? FindNextObservedSlot(
            MarketSessionSchedule schedule,
            TimeZoneInfo timeZone,
            DateTime previousBarUtc,
            DateTime previousLocal,
            ObservedIntradayPattern pattern,
            bool useExtendedHours)
        {
            var previousDate = DateOnly.FromDateTime(previousLocal.Date);
            var previousSlot = TimeOnly.FromDateTime(previousLocal);

            foreach (var day in schedule.Days
                         .Where(x => x.IsTradingDay && x.Date >= previousDate)
                         .OrderBy(x => x.Date))
            {
                if (day.Date == previousDate)
                {
                    if (pattern.NextSlotBySlot.TryGetValue(previousSlot, out var mappedNextSameDay))
                    {
                        var candidate = day.Date.ToDateTime(mappedNextSameDay);
                        var candidateUtc = TimeZoneInfo.ConvertTimeToUtc(candidate, timeZone);

                        if (candidateUtc > previousBarUtc &&
                            IsWithinAllowedSessions(day, candidateUtc, useExtendedHours))
                        {
                            return candidateUtc;
                        }
                    }

                    var laterSlots = pattern.AllowedSlots
                        .Where(x => x > previousSlot)
                        .OrderBy(x => x)
                        .ToList();

                    foreach (var laterSlot in laterSlots)
                    {
                        var candidate = day.Date.ToDateTime(laterSlot);
                        var candidateUtc = TimeZoneInfo.ConvertTimeToUtc(candidate, timeZone);

                        if (candidateUtc > previousBarUtc &&
                            IsWithinAllowedSessions(day, candidateUtc, useExtendedHours))
                        {
                            return candidateUtc;
                        }
                    }

                    continue;
                }

                var openingCandidate = day.Date.ToDateTime(pattern.OpeningSlot);
                var openingCandidateUtc = TimeZoneInfo.ConvertTimeToUtc(openingCandidate, timeZone);

                if (openingCandidateUtc > previousBarUtc &&
                    IsWithinAllowedSessions(day, openingCandidateUtc, useExtendedHours))
                {
                    return openingCandidateUtc;
                }

                foreach (var slot in pattern.AllowedSlots.OrderBy(x => x))
                {
                    var candidate = day.Date.ToDateTime(slot);
                    var candidateUtc = TimeZoneInfo.ConvertTimeToUtc(candidate, timeZone);

                    if (candidateUtc > previousBarUtc &&
                        IsWithinAllowedSessions(day, candidateUtc, useExtendedHours))
                    {
                        return candidateUtc;
                    }
                }
            }

            return null;
        }

        private static DateTime? FindNextFallbackSlot(
            MarketSessionSchedule schedule,
            TimeZoneInfo timeZone,
            Timeframe timeframe,
            DateTime previousBarUtc,
            DateOnly previousDate,
            bool useExtendedHours)
        {
            var step = timeframe.ToTimeSpan();

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

        private static bool IsExpectedWeekendTransition(
            DateTime previousLocal,
            DateTime currentLocal)
        {
            if (previousLocal.Date == currentLocal.Date)
                return false;

            return previousLocal.DayOfWeek == DayOfWeek.Friday &&
                   (currentLocal.DayOfWeek == DayOfWeek.Monday || currentLocal.DayOfWeek == DayOfWeek.Sunday);
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

        private sealed record ObservedIntradayPattern(
            IReadOnlyList<TimeOnly> AllowedSlots,
            IReadOnlyDictionary<TimeOnly, TimeOnly> NextSlotBySlot,
            TimeOnly OpeningSlot);
    }
}
