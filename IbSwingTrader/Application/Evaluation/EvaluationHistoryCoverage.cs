using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Application.Evaluation
{
    public readonly record struct EvaluationMissingRange(DateTime Start, DateTime End);

    public static class EvaluationHistoryCoverage
    {
        // Missing regular-session data is not evidence that no entry/stop occurred.
        // Zero-volume bars cover a slot but never establish execution.
        public static List<EvaluationMissingRange> MissingRegularRanges(IReadOnlyList<Candle> grid,
            MarketSessionSchedule schedule, DateTime start, DateTime end)
        {
            var present = grid.Where(TradeObservation.HasValidOhlc).Select(x => x.Time).ToHashSet();
            var result = new List<EvaluationMissingRange>();
            foreach (var day in schedule.Days.Where(x => x.IsTradingDay))
            foreach (var session in day.Sessions.Where(x => x.Type == MarketSessionType.Regular))
            {
                var from = MarketTime.FromUtc(session.StartUtc, schedule.TimeZoneId);
                var until = MarketTime.FromUtc(session.EndUtc, schedule.TimeZoneId);
                DateTime? first = null, last = null;
                for (var slot = from; slot.AddMinutes(5) <= until && slot.AddMinutes(5) <= end; slot = slot.AddMinutes(5))
                {
                    if (slot < start || present.Contains(slot))
                        continue;
                    first ??= slot;
                    last = slot;
                }
                if (first.HasValue)
                    result.Add(new(first.Value, last!.Value.AddMinutes(5)));
            }
            return result.OrderBy(x => x.Start).ToList();
        }
    }
}
