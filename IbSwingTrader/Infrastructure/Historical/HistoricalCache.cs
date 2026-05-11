using System.Text;
using System.Text.Json;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Infrastructure.Historical
{
    public class HistoricalCache : IHistoricalCache
    {
        private readonly string _folder;
        private readonly ITextLogger _logger;
        private readonly IFeatureEngine _featureEngine;
        private const int RecentDailySeriesLength = 12;
        private const int RecentWeeklySeriesLength = 10;
        private const int RecentH4SeriesLength = 16;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public HistoricalCache(
            IAgentPathService pathService,
            IFeatureEngine featureEngine,
            ITextLogger logger)
        {
            _folder = pathService.GetCacheFolder();
            _featureEngine = featureEngine;
            _logger = logger;

            Directory.CreateDirectory(_folder);
        }

        public bool TryLoad(
            string symbol,
            Timeframe timeframe,
            out List<Candle>? candles)
        {
            candles = null;

            var path = BuildPath(symbol);

            if (!File.Exists(path))
                return false;

            try
            {
                var json = File.ReadAllText(path);
                var file = JsonSerializer.Deserialize<HistoricalSymbolCache>(json, JsonOptions);

                if (file == null)
                    return false;

                var timeframeKey = BuildTimeframeKey(timeframe);

                if (!file.Timeframes.TryGetValue(timeframeKey, out var storedCandles) ||
                    storedCandles == null ||
                    storedCandles.Count == 0)
                {
                    return false;
                }

                candles = storedCandles
                    .Select(NormalizeCandleTime)
                    .OrderBy(x => x.Time)
                    .ToList();

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Historical cache load failed: {path}. {ex.Message}");
                return false;
            }
        }

        public void Save(
            string symbol,
            Timeframe timeframe,
            List<Candle> candles)
        {
            var path = BuildPath(symbol);

            try
            {
                HistoricalSymbolCache file;

                if (File.Exists(path))
                {
                    var existingJson = File.ReadAllText(path);
                    file = JsonSerializer.Deserialize<HistoricalSymbolCache>(existingJson, JsonOptions)
                           ?? new HistoricalSymbolCache();
                }
                else
                {
                    file = new HistoricalSymbolCache();
                }

                file.Symbol = symbol;

                var timeframeKey = BuildTimeframeKey(timeframe);

                file.Timeframes[timeframeKey] = candles
                    .Select(NormalizeCandleTime)
                    .GroupBy(x => new { x.Timeframe, x.Time })
                    .Select(g => g.First())
                    .OrderBy(x => x.Time)
                    .ToList();

                file.Coverage[timeframeKey] = BuildCoverage(file.Timeframes[timeframeKey]);

                if (file.Timeframes.TryGetValue(nameof(Timeframe.H4), out var h4Candles) &&
                    h4Candles != null &&
                    h4Candles.Count > 0)
                {
                    file.PatternSnapshot = BuildPatternSnapshot(h4Candles);
                }

                var json = JsonSerializer.Serialize(file, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                _logger.Error($"Historical cache save failed: {path}. {ex.Message}");
            }
        }

        private string BuildPath(string symbol)
        {
            var safeSymbol = SanitizeFileNamePart(symbol);
            return Path.Combine(_folder, $"{safeSymbol}.json");
        }

        private static string BuildTimeframeKey(Timeframe timeframe)
        {
            return timeframe.ToString();
        }

        private static string SanitizeFileNamePart(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);

            foreach (var ch in value)
            {
                if (invalidChars.Contains(ch))
                    sb.Append('_');
                else
                    sb.Append(ch);
            }

            return sb.ToString();
        }

        private static Candle NormalizeCandleTime(Candle candle)
        {
            candle.Time = MarketTime.Normalize(candle.Time);
            return candle;
        }

        private HistoricalTimeframeCoverage BuildCoverage(List<Candle> candles)
        {
            if (candles.Count == 0)
            {
                return new HistoricalTimeframeCoverage();
            }

            return new HistoricalTimeframeCoverage
            {
                Count = candles.Count,
                FirstTime = candles[0].Time,
                LastTime = candles[^1].Time,
                SpanDays = Math.Max(0, (candles[^1].Time.Date - candles[0].Time.Date).Days)
            };
        }

        private CachedPatternSnapshot BuildPatternSnapshot(List<Candle> candles)
        {
            var scanIndex = candles.Count - 1;

            var dailyMa = BuildRecentDailySeries(candles, scanIndex, x => x.DailyMaSignedDistancePct);
            var dailyBbMid = BuildRecentDailySeries(candles, scanIndex, x => x.DailyBollingerMidDistancePct);
            var dailyBbUpper = BuildRecentDailySeries(candles, scanIndex, x => x.DailyBollingerUpperDistancePct);
            var dailyBbWidth = BuildRecentDailySeries(candles, scanIndex, x => x.DailyBollingerBandWidthPct);
            var dailyRsi = BuildRecentDailySeries(candles, scanIndex, x => x.DailyRSI14);
            var dailyMacd = BuildRecentDailySeries(candles, scanIndex, x => x.DailyMACDLineMinusSignal);
            var weeklyMa = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyMaSignedDistancePct);
            var weeklyBbMid = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyBollingerMidDistancePct);
            var weeklyBbUpper = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyBollingerUpperDistancePct);
            var weeklyBbWidth = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyBollingerBandWidthPct);
            var weeklyRsi = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyRSI14);
            var weeklyMacd = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyMACDLineMinusSignal);
            var h4Ma = BuildRecentH4Series(candles, scanIndex, x => x.H4MaSignedDistancePct);
            var h4BbMid = BuildRecentH4Series(candles, scanIndex, x => x.H4BollingerMidDistancePct);
            var h4BbUpper = BuildRecentH4Series(candles, scanIndex, x => x.H4BollingerUpperDistancePct);
            var h4BbWidth = BuildRecentH4Series(candles, scanIndex, x => x.H4BollingerBandWidthPct);
            var h4Rsi = BuildRecentH4Series(candles, scanIndex, x => x.RSI14);
            var h4Macd = BuildRecentH4Series(candles, scanIndex, x => x.MACDLineMinusSignal);

            return new CachedPatternSnapshot
            {
                RecentDailyMaSeries = dailyMa,
                RecentDailyBbMidDistanceSeries = dailyBbMid,
                RecentDailyBbUpperDistanceSeries = dailyBbUpper,
                RecentDailyBbWidthSeries = dailyBbWidth,
                RecentDailyRsiSeries = dailyRsi,
                RecentDailyMacdSeries = dailyMacd,
                RecentWeeklyMaSeries = weeklyMa,
                RecentWeeklyBbMidDistanceSeries = weeklyBbMid,
                RecentWeeklyBbUpperDistanceSeries = weeklyBbUpper,
                RecentWeeklyBbWidthSeries = weeklyBbWidth,
                RecentWeeklyRsiSeries = weeklyRsi,
                RecentWeeklyMacdSeries = weeklyMacd,
                RecentH4MaSeries = h4Ma,
                RecentH4BbMidDistanceSeries = h4BbMid,
                RecentH4BbUpperDistanceSeries = h4BbUpper,
                RecentH4BbWidthSeries = h4BbWidth,
                RecentH4RsiSeries = h4Rsi,
                RecentH4MacdSeries = h4Macd,
                DailyMaSlope = CalculateSlope(dailyMa),
                DailyRsiSlope = CalculateSlope(dailyRsi),
                H4MaSlope = CalculateSlope(h4Ma),
                H4RsiSlope = CalculateSlope(h4Rsi),
                DailyRsiUpMoves = CountUpMoves(dailyRsi),
                H4RsiUpMoves = CountUpMoves(h4Rsi),
                H4MaRollingOver = IsRollingOver(h4Ma),
                H4RsiExhausted = IsExhausted(h4Rsi, 78m)
            };
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
            Func<FeatureSet, decimal?> selector)
        {
            var indexes = new List<int>();
            var usedWeeks = new HashSet<DateTime>();

            for (var i = scanIndex; i >= 0; i--)
            {
                var week = StartOfWeek(candles[i].Time);
                if (!usedWeeks.Add(week))
                    continue;

                indexes.Add(i);
                if (indexes.Count >= RecentWeeklySeriesLength)
                    break;
            }

            indexes.Reverse();
            return [.. indexes
                .Select(i => selector(_featureEngine.Calculate(candles, i + 1)))
                .Where(x => x.HasValue)
                .Select(x => decimal.Round(x!.Value, 2, MidpointRounding.AwayFromZero))];
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

        private static DateTime StartOfWeek(DateTime time)
        {
            var day = (int)time.DayOfWeek;
            var delta = day == 0 ? 6 : day - 1;
            return time.Date.AddDays(-delta);
        }

        private static DateTime StartOfH4Bucket(DateTime time)
            => new(time.Year, time.Month, time.Day, (time.Hour / 4) * 4, 0, 0, time.Kind);

        private static decimal CalculateSlope(List<decimal> series)
            => series.Count >= 2 ? decimal.Round(series[^1] - series[0], 2, MidpointRounding.AwayFromZero) : 0m;

        private static int CountUpMoves(List<decimal> series)
            => series.Count < 2 ? 0 : series.Zip(series.Skip(1), (a, b) => b > a ? 1 : 0).Sum();

        private static bool IsRollingOver(List<decimal> series)
            => series.Count >= 3 && series[^1] < series[^2] && series[^2] <= series[^3];

        private static bool IsExhausted(List<decimal> series, decimal threshold)
            => series.Count >= 3 &&
               series[^1] >= threshold &&
               series[^1] <= series[^2];
    }
}
