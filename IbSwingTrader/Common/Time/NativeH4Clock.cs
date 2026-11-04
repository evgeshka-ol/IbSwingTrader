using IbSwingTrader.Domain.Market;

namespace IbSwingTrader.Common.Time
{
    // Native timestamps remain untouched. Infer the broker's UTC phase from
    // observed starts, then project it through each date's exchange UTC offset.
    public sealed class NativeH4Clock
    {
        private static readonly long Step = TimeSpan.FromHours(4).Ticks;
        private readonly long _utcPhase;
        private readonly Dictionary<DateTime, DateTime> _nextObserved;
        private readonly Dictionary<DateTime, long> _dayPhases;
        private readonly IReadOnlyDictionary<DateTime, DateTime>? _sessionCloses;

        private NativeH4Clock(long utcPhase, List<DateTime> observed,
            IReadOnlyDictionary<DateTime, DateTime>? sessionCloses)
        {
            _utcPhase = utcPhase;
            _nextObserved = observed.Zip(observed.Skip(1), (a,b) => (Start:a, Next:b))
                .Where(x => x.Start.Date == x.Next.Date).ToDictionary(x => x.Start, x => x.Next);
            _dayPhases = observed.Where(IsFullBarStart).GroupBy(x => x.Date)
                .ToDictionary(x => x.Key, x => Phase(x));
            _sessionCloses = sessionCloses;
        }

        public static NativeH4Clock From(IEnumerable<Candle>? history = null, DateTime? asOf = null,
            IReadOnlyDictionary<DateTime, DateTime>? sessionCloses = null)
        {
            var starts = (history ?? []).Select(x => x.Time)
                .Where(x => !asOf.HasValue || x <= asOf.Value).Distinct().OrderBy(x => x).ToList();
            // The 04:00 opening stub is not evidence of the full-bar phase.
            var regular = starts.Where(IsFullBarStart).TakeLast(64).ToList();
            var phase = regular.Count == 0 ? 0 : Phase(regular.Where(x => x.Date == regular[^1].Date));
            return new(phase, starts, sessionCloses);
        }

        private static bool IsFullBarStart(DateTime at) => at.TimeOfDay > TimeSpan.FromHours(4) &&
            at.TimeOfDay < TimeSpan.FromHours(20);

        private static long Phase(IEnumerable<DateTime> starts)
        {
            var phases = starts.Select(x => MarketTime.ToUtc(x).Ticks % Step).ToList();
            return phases.GroupBy(x => x).OrderByDescending(x => x.Count())
                .ThenByDescending(x => phases.LastIndexOf(x.Key)).First().Key;
        }

        public IEnumerable<DateTime> TradingDates => _sessionCloses?.Keys ?? Enumerable.Empty<DateTime>();

        public DateTime SessionClose(DateTime day) => _sessionCloses != null &&
            _sessionCloses.TryGetValue(day.Date, out var close) ? close : day.Date.AddHours(20);

        public List<DateTime> Starts(DateTime day)
        {
            var open = day.Date.AddHours(4);
            var close = SessionClose(day);
            if (close <= open) return [];
            var result = new List<DateTime> { open };
            var utc = MarketTime.ToUtc(open);
            var phase = _dayPhases.GetValueOrDefault(day.Date, _utcPhase);
            var next = new DateTime(utc.Ticks - utc.Ticks % Step + phase, DateTimeKind.Utc);
            if (next <= utc) next = next.AddHours(4);
            for (; next < MarketTime.ToUtc(close); next = next.AddHours(4))
            {
                var local = MarketTime.FromUtc(next);
                if (local > open) result.Add(local);
            }
            return result;
        }

        public DateTime? CurrentStart(DateTime at) => at < at.Date.AddHours(4) || at >= SessionClose(at)
            ? null : Starts(at).Last(x => x <= at);

        public DateTime End(DateTime start)
        {
            if (_sessionCloses != null && _sessionCloses.TryGetValue(start.Date, out var configuredClose) &&
                start >= configuredClose)
                return DateTime.MaxValue;
            // Preserve non-session synthetic/legacy starts' ordinary four-hour bound.
            if (start.TimeOfDay < TimeSpan.FromHours(4) || start.TimeOfDay >= TimeSpan.FromHours(20))
                return start.AddHours(4);
            var end = start.AddHours(4);
            if (start.TimeOfDay == TimeSpan.FromHours(4))
                end = Starts(start).FirstOrDefault(x => x > start, SessionClose(start));
            if (_nextObserved.TryGetValue(start, out var nextObserved) && nextObserved <= start.AddHours(4))
                end = nextObserved;
            return end < SessionClose(start) ? end : SessionClose(start);
        }

        public DateTime LastClosedStart(IEnumerable<DateTime> tradingDates, DateTime at) => tradingDates
            .SelectMany(Starts).Where(x => End(x) <= at).DefaultIfEmpty(DateTime.MinValue).Max();

    }
}
