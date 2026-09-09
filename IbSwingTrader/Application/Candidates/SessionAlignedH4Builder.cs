namespace IbSwingTrader.Application.Candidates
{
    /// <summary>
    /// Builds the session-aligned intraday view used by the user's charts.
    /// TWS H4 remains the canonical four-hour series; this is a separate view.
    /// </summary>
    public static class SessionAlignedH4Builder
    {
        private static readonly TimeSpan RegularOpen = new(9, 30, 0);

        public static List<Candle> Build(IEnumerable<Candle> m15Candles, DateTime scanTime)
        {
            var source = m15Candles
                .Where(x => x.Time <= scanTime)
                .OrderBy(x => x.Time)
                .ToList();
            var result = new List<Candle>();

            foreach (var day in source.GroupBy(x => x.Time.Date))
            {
                var date = day.Key;
                AddBucket(result, day.Where(x => x.Time.TimeOfDay >= new TimeSpan(8, 0, 0) &&
                                                  x.Time.TimeOfDay < RegularOpen), date.AddHours(8));
                AddBucket(result, day.Where(x => x.Time.TimeOfDay >= RegularOpen &&
                                                  x.Time.TimeOfDay < new TimeSpan(13, 30, 0)),
                    date.AddHours(9).AddMinutes(30));
                AddBucket(result, day.Where(x => x.Time.TimeOfDay >= new TimeSpan(13, 30, 0) &&
                                                  x.Time.TimeOfDay < new TimeSpan(16, 0, 0)),
                    date.AddHours(13).AddMinutes(30));
            }

            return result.OrderBy(x => x.Time).ToList();
        }

        private static void AddBucket(List<Candle> result, IEnumerable<Candle> source, DateTime start)
        {
            var candles = source.OrderBy(x => x.Time).ToList();
            if (candles.Count == 0)
                return;

            result.Add(new Candle
            {
                Timeframe = Timeframe.H4,
                Time = start,
                Open = candles[0].Open,
                High = candles.Max(x => x.High),
                Low = candles.Min(x => x.Low),
                Close = candles[^1].Close,
                Volume = candles.Sum(x => x.Volume)
            });
        }
    }
}
