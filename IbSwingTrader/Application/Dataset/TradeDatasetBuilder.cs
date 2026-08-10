
namespace IbSwingTrader.Application.Dataset
{
    public class TradeDatasetBuilder(
        IFeatureEngine featureEngine,
        ITextLogger logger,
        INumberTextFormatter numberFormatter) : ITradeDatasetBuilder
    {
        private readonly IFeatureEngine _featureEngine = featureEngine;
        private readonly ITextLogger _logger = logger;
        private readonly INumberTextFormatter _fmt = numberFormatter;

        // Same window lengths as the scanner (CandidateFinder.cs) and the evaluation dataset builder,
        // so Recent*Series here is directly comparable to candidates.csv.
        private const int RecentDailySeriesLength = RecentSeriesWindow.Daily;
        private const int RecentWeeklySeriesLength = RecentSeriesWindow.Weekly;
        private const int RecentH4SeriesLength = RecentSeriesWindow.H4;

        public List<TradeDatasetRow> Build(
            List<TradeRecord> trades,
            List<Candle> candles)
        {
            var rows = new List<TradeDatasetRow>();

            if (candles == null || candles.Count == 0)
                return rows;

            for (int t = 0; t < trades.Count; t++)
            {
                var trade = trades[t];

                var entryIndex = FindBarIndex(candles, trade.EntryTimeMarket);
                var exitIndex = FindBarIndex(candles, trade.ExitTimeMarket);

                if (entryIndex < 0)
                {
                    _logger.Info(
                        $"Trade {t}: Entry time {trade.EntryTimeMarket} is before first candle {candles[0].Time}");
                    continue;
                }

                if (exitIndex < 0)
                {
                    _logger.Info(
                        $"Trade {t}: Exit time {trade.ExitTimeMarket} is before first candle {candles[0].Time}");
                    continue;
                }

                if (exitIndex < entryIndex)
                {
                    _logger.Info(
                        $"Trade {t}: Exit index {exitIndex} is before entry index {entryIndex}");
                    continue;
                }

                var entryTime = trade.EntryTimeMarket;
                var exitTime = trade.ExitTimeMarket;

                var holdHours = (decimal)(exitTime - entryTime).TotalHours;
                if (holdHours < 4m)
                {
                    _logger.Info(
                        $"Trade {t}: Hold time {_fmt.Hours(holdHours)} hours, less than 4 hours");
                    continue;
                }

                var candlePriceReal = candles[entryIndex].Close;
                var splitFactor = DetectSplitFactor(trade.EntryPrice, candlePriceReal);

                var entryPrice = trade.EntryPrice / splitFactor;
                var exitPrice = trade.ExitPrice / splitFactor;
                var side = trade.IsShort ? -1m : 1m;

                var row = new TradeDatasetRow
                {
                    Ticker = trade.Ticker,
                    IsShort = trade.IsShort,

                    EntryTimeMarket = entryTime,
                    EntryPrice = entryPrice,
                    EntryQuantity = trade.EntryQuantity,

                    ExitTimeMarket = exitTime,
                    ExitPrice = exitPrice,
                    ExitQuantity = trade.ExitQuantity,

                    ProfitPercent = side * (exitPrice - entryPrice) / entryPrice * 100m,
                    HoldDays = (exitTime.Date - entryTime.Date).Days
                };

                FillRecentSeries(row, candles, entryIndex);

                rows.Add(row);
            }

            return rows
                .OrderBy(x => x.Ticker)
                .ThenByDescending(x => x.ProfitPercent)
                .ThenBy(x => x.HoldDays)
                .ToList();
        }

        private void FillRecentSeries(
            TradeDatasetRow row,
            List<Candle> candles,
            int entryIndex)
        {
            row.RecentDailyBbUpperBandSeries = BuildRecentDailySeries(candles, entryIndex, x => x.DailyBollingerUpperBand);
            row.RecentDailyBbMidBandSeries = BuildRecentDailySeries(candles, entryIndex, x => x.DailyBollingerMidBand);
            row.RecentDailyBbLowerBandSeries = BuildRecentDailySeries(candles, entryIndex, x => x.DailyBollingerLowerBand);
            row.RecentDailyRsiSeries = BuildRecentDailySeries(candles, entryIndex, x => x.DailyRSI14);
            row.RecentDailyMacdLineSeries = BuildRecentDailySeries(candles, entryIndex, x => x.DailyMACDLine);
            row.RecentDailyMacdSignalSeries = BuildRecentDailySeries(candles, entryIndex, x => x.DailyMACDSignal);
            row.RecentDailyMacdHistogramSeries = BuildRecentDailySeries(candles, entryIndex, x => x.DailyMACDHistogram);

            row.RecentWeeklyBbUpperBandSeries = BuildRecentWeeklySeries(candles, entryIndex, x => x.WeeklyBollingerUpperBand ?? 0m);
            row.RecentWeeklyBbMidBandSeries = BuildRecentWeeklySeries(candles, entryIndex, x => x.WeeklyBollingerMidBand ?? 0m);
            row.RecentWeeklyBbLowerBandSeries = BuildRecentWeeklySeries(candles, entryIndex, x => x.WeeklyBollingerLowerBand ?? 0m);
            row.RecentWeeklyRsiSeries = BuildRecentWeeklySeries(candles, entryIndex, x => x.WeeklyRSI14 ?? 0m);
            row.RecentWeeklyMacdLineSeries = BuildRecentWeeklySeries(candles, entryIndex, x => x.WeeklyMACDLine ?? 0m);
            row.RecentWeeklyMacdSignalSeries = BuildRecentWeeklySeries(candles, entryIndex, x => x.WeeklyMACDSignal ?? 0m);
            row.RecentWeeklyMacdHistogramSeries = BuildRecentWeeklySeries(candles, entryIndex, x => x.WeeklyMACDHistogram ?? 0m);

            row.RecentH4BbUpperBandSeries = BuildRecentH4Series(candles, entryIndex, x => x.H4BollingerUpperBand);
            row.RecentH4BbMidBandSeries = BuildRecentH4Series(candles, entryIndex, x => x.H4BollingerMidBand);
            row.RecentH4BbLowerBandSeries = BuildRecentH4Series(candles, entryIndex, x => x.H4BollingerLowerBand);
            row.RecentH4RsiSeries = BuildRecentH4Series(candles, entryIndex, x => x.RSI14);
            row.RecentH4MacdLineSeries = BuildRecentH4Series(candles, entryIndex, x => x.MACDLine);
            row.RecentH4MacdSignalSeries = BuildRecentH4Series(candles, entryIndex, x => x.MACDSignal);
            row.RecentH4MacdHistogramSeries = BuildRecentH4Series(candles, entryIndex, x => x.MACDHistogram);
        }

        private List<decimal> BuildRecentDailySeries(
            List<Candle> candles,
            int scanIndex,
            Func<FeatureSet, decimal> selector)
        {
            var indexes = new List<int>();
            var usedDays = new HashSet<DateTime>();

            for (var i = scanIndex; i >= 0; i--)
            {
                var day = candles[i].Time.Date;
                if (!usedDays.Add(day))
                    continue;

                indexes.Add(i);
                if (indexes.Count >= RecentDailySeriesLength)
                    break;
            }

            indexes.Reverse();
            return [.. indexes.Select(i => decimal.Round(selector(_featureEngine.Calculate(candles, i + 1)), 2, MidpointRounding.AwayFromZero))];
        }

        private List<decimal> BuildRecentWeeklySeries(
            List<Candle> candles,
            int scanIndex,
            Func<FeatureSet, decimal> selector)
        {
            var indexes = new List<int>();
            var usedWeeks = new HashSet<DateTime>();

            for (var i = scanIndex; i >= 0; i--)
            {
                var weekStart = GetWeekStart(candles[i].Time);
                if (!usedWeeks.Add(weekStart))
                    continue;

                indexes.Add(i);
                if (indexes.Count >= RecentWeeklySeriesLength)
                    break;
            }

            indexes.Reverse();
            return [.. indexes.Select(i => decimal.Round(selector(_featureEngine.Calculate(candles, i + 1)), 2, MidpointRounding.AwayFromZero))];
        }

        private List<decimal> BuildRecentH4Series(
            List<Candle> candles,
            int scanIndex,
            Func<FeatureSet, decimal> selector)
        {
            var indexes = new List<int>();
            var usedBuckets = new HashSet<DateTime>();

            for (var i = scanIndex; i >= 0; i--)
            {
                var bucket = StartOfH4Bucket(candles[i].Time);
                if (!usedBuckets.Add(bucket))
                    continue;

                indexes.Add(i);
                if (indexes.Count >= RecentH4SeriesLength)
                    break;
            }

            indexes.Reverse();
            return [.. indexes.Select(i => decimal.Round(selector(_featureEngine.Calculate(candles, i + 1)), 2, MidpointRounding.AwayFromZero))];
        }

        private static DateTime StartOfH4Bucket(DateTime time)
        {
            var hour = time.Hour - (time.Hour % 4);
            return new DateTime(time.Year, time.Month, time.Day, hour, 0, 0, time.Kind);
        }

        private static DateTime GetWeekStart(DateTime time)
        {
            var date = time.Date;
            int diff = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
            return date.AddDays(-diff);
        }

        private static decimal DetectSplitFactor(decimal tradePrice, decimal candlePrice)
        {
            if (candlePrice <= 0m)
                return 1m;

            var ratio = tradePrice / candlePrice;
            var rounded = Math.Round(ratio);

            if (rounded >= 2m &&
                rounded <= 20m &&
                Math.Abs(ratio - rounded) < 0.2m)
            {
                return rounded;
            }

            return 1m;
        }

        private static int FindBarIndex(List<Candle> candles, DateTime time)
        {
            int left = 0;
            int right = candles.Count - 1;

            while (left <= right)
            {
                int mid = left + ((right - left) >> 1);

                if (candles[mid].Time <= time)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            return right;
        }
    }
}
