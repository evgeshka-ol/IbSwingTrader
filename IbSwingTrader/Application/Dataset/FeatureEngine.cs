
namespace IbSwingTrader.Application.Dataset
{
    public class FeatureEngine : IFeatureEngine
    {
        public FeatureSet Calculate(List<Candle> candles, int index)
        {
            ArgumentNullException.ThrowIfNull(candles);

            if (index <= 0 || index > candles.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return new FeatureSet
            {
                DistanceTo20dHigh = CalcDistanceTo20dHigh(candles, index),
                DistanceTo52wHigh = CalcDistanceTo52wHigh(candles, index),

                // --- 4H ---
                H4MaSignedDistancePct = CalcSmaSignedDistancePctFromCandles(candles, index, 50),
                RSI14 = CalcRsiFromCandles(candles, index, 14),
                MACDLineMinusSignal = CalcMacdLineMinusSignalFromCandles(candles, index),

                // --- Daily ---
                DailyMaSignedDistancePct = CalcDailySmaSignedDistancePct(candles, index, 20),
                DailyBollingerUpperDistancePct = CalcDailyBollingerUpperDistancePct(candles, index, 20, 2m),
                DailyBollingerBandWidthPct = CalcDailyBollingerBandWidthPct(candles, index, 20, 2m),
                DailyRSI14 = CalcDailyRsi14(candles, index),
                DailyMACDLineMinusSignal = CalcDailyMacdLineMinusSignal(candles, index),

                // --- Weekly ---
                WeeklyMaSignedDistancePct = CalcWeeklySmaSignedDistancePct(candles, index, 20),
                WeeklyBollingerUpperDistancePct = CalcWeeklyBollingerUpperDistancePct(candles, index, 20, 2m),
                WeeklyBollingerBandWidthPct = CalcWeeklyBollingerBandWidthPct(candles, index, 20, 2m),
                WeeklyRSI14 = CalcWeeklyRsi14(candles, index),
                WeeklyMACDLineMinusSignal = CalcWeeklyMacdLineMinusSignal(candles, index)
            };
        }

        public FeatureSet CalculateLast(List<Candle> candles)
        {
            ArgumentNullException.ThrowIfNull(candles);

            if (candles.Count == 0)
                throw new ArgumentException("Candles collection is empty.", nameof(candles));

            return Calculate(candles, candles.Count);
        }

        private static decimal CalcDistanceTo20dHigh(List<Candle> candles, int i)
        {
            const int days = 20;
            var dailyBars = BuildDailyBars(candles, i);

            if (dailyBars.Count == 0)
                return 0m;

            int start = Math.Max(0, dailyBars.Count - days);

            decimal highest = decimal.MinValue;
            for (int k = start; k < dailyBars.Count; k++)
            {
                if (dailyBars[k].High > highest)
                    highest = dailyBars[k].High;
            }

            if (highest <= 0m)
                return 0m;

            var close = dailyBars[^1].Close;
            return (close - highest) / highest * 100m;
        }

        private static decimal CalcDistanceTo52wHigh(List<Candle> candles, int i)
        {
            const int weeks = 52;
            var weeklyBars = BuildWeeklyBars(candles, i);

            if (weeklyBars.Count == 0)
                return 0m;

            int start = Math.Max(0, weeklyBars.Count - weeks);

            decimal highest = decimal.MinValue;
            for (int k = start; k < weeklyBars.Count; k++)
            {
                if (weeklyBars[k].High > highest)
                    highest = weeklyBars[k].High;
            }

            if (highest <= 0m)
                return 0m;

            var close = weeklyBars[^1].Close;
            return (close - highest) / highest * 100m;
        }

        private static decimal CalcSmaSignedDistancePctFromCandles(
            List<Candle> candles,
            int i,
            int length)
        {
            if (i <= 0 || i > candles.Count)
                return 0m;

            if (i < length)
                return 0m;

            decimal sum = 0m;
            for (int k = i - length; k < i; k++)
                sum += candles[k].Close;

            var sma = sum / length;
            if (sma == 0m)
                return 0m;

            var close = candles[i - 1].Close;
            return (close - sma) / sma * 100m;
        }

        private static decimal CalcDailySmaSignedDistancePct(
            List<Candle> candles,
            int i,
            int length)
        {
            var dailyCloses = BuildDailyCloses(candles, i);

            if (dailyCloses.Count < length)
                return 0m;

            decimal sum = 0m;
            for (int k = dailyCloses.Count - length; k < dailyCloses.Count; k++)
                sum += dailyCloses[k];

            var sma = sum / length;
            if (sma == 0m)
                return 0m;

            var close = dailyCloses[^1];
            return (close - sma) / sma * 100m;
        }

        private static decimal CalcDailyBollingerUpperDistancePct(
            List<Candle> candles,
            int i,
            int length,
            decimal stdDevMultiplier)
        {
            return CalcBollingerUpperDistancePctFromSeries(BuildDailyCloses(candles, i), length, stdDevMultiplier) ?? 0m;
        }

        private static decimal CalcDailyBollingerBandWidthPct(
            List<Candle> candles,
            int i,
            int length,
            decimal stdDevMultiplier)
        {
            return CalcBollingerBandWidthPctFromSeries(BuildDailyCloses(candles, i), length, stdDevMultiplier) ?? 0m;
        }

        private static decimal? CalcWeeklySmaSignedDistancePct(
            List<Candle> candles,
            int i,
            int length)
        {
            var weeklyCloses = BuildWeeklyCloses(candles, i);

            if (weeklyCloses.Count < length)
                return null;

            decimal sum = 0m;
            for (int k = weeklyCloses.Count - length; k < weeklyCloses.Count; k++)
                sum += weeklyCloses[k];

            var sma = sum / length;
            if (sma == 0m)
                return null;

            var close = weeklyCloses[^1];
            return (close - sma) / sma * 100m;
        }

        private static decimal? CalcWeeklyBollingerUpperDistancePct(
            List<Candle> candles,
            int i,
            int length,
            decimal stdDevMultiplier)
        {
            return CalcBollingerUpperDistancePctFromSeries(BuildWeeklyCloses(candles, i), length, stdDevMultiplier);
        }

        private static decimal? CalcWeeklyBollingerBandWidthPct(
            List<Candle> candles,
            int i,
            int length,
            decimal stdDevMultiplier)
        {
            return CalcBollingerBandWidthPctFromSeries(BuildWeeklyCloses(candles, i), length, stdDevMultiplier);
        }

        private static decimal CalcRsiFromCandles(
            List<Candle> candles,
            int i,
            int length)
        {
            if (i <= 0 || i > candles.Count)
                return 0m;

            if (i < length + 1)
                return 0m;

            decimal gainSum = 0m;
            decimal lossSum = 0m;

            for (int k = i - length; k < i; k++)
            {
                var change = candles[k].Close - candles[k - 1].Close;

                if (change > 0)
                    gainSum += change;
                else
                    lossSum -= change;
            }

            var avgGain = gainSum / length;
            var avgLoss = lossSum / length;

            if (avgLoss == 0m)
                return 100m;

            var rs = avgGain / avgLoss;
            return 100m - (100m / (1m + rs));
        }

        private static decimal CalcDailyRsi14(List<Candle> candles, int i)
        {
            return CalcRsiFromSeries(BuildDailyCloses(candles, i), 14);
        }

        private static decimal? CalcWeeklyRsi14(List<Candle> candles, int i)
        {
            return CalcRsiFromSeriesNullable(BuildWeeklyCloses(candles, i), 14);
        }

        private static decimal CalcRsiFromSeries(List<decimal> closes, int length)
        {
            if (closes.Count < length + 1)
                return 0m;

            decimal gainSum = 0m;
            decimal lossSum = 0m;

            int start = closes.Count - length;

            for (int k = start; k < closes.Count; k++)
            {
                var change = closes[k] - closes[k - 1];

                if (change > 0)
                    gainSum += change;
                else
                    lossSum -= change;
            }

            var avgGain = gainSum / length;
            var avgLoss = lossSum / length;

            if (avgLoss == 0m)
                return 100m;

            var rs = avgGain / avgLoss;
            return 100m - (100m / (1m + rs));
        }

        private static decimal? CalcRsiFromSeriesNullable(List<decimal> closes, int length)
        {
            if (closes.Count < length + 1)
                return null;

            decimal gainSum = 0m;
            decimal lossSum = 0m;

            int start = closes.Count - length;

            for (int k = start; k < closes.Count; k++)
            {
                var change = closes[k] - closes[k - 1];

                if (change > 0)
                    gainSum += change;
                else
                    lossSum -= change;
            }

            var avgGain = gainSum / length;
            var avgLoss = lossSum / length;

            if (avgLoss == 0m)
                return 100m;

            var rs = avgGain / avgLoss;
            return 100m - (100m / (1m + rs));
        }

        private static decimal CalcMacdLineMinusSignalFromCandles(List<Candle> candles, int i)
        {
            if (i <= 0 || i > candles.Count)
                return 0m;

            var closes = new List<decimal>(i);
            for (int k = 0; k < i; k++)
                closes.Add(candles[k].Close);

            return CalcMacdLineMinusSignalFromSeries(closes) ?? 0m;
        }

        private static decimal CalcDailyMacdLineMinusSignal(List<Candle> candles, int i)
        {
            return CalcMacdLineMinusSignalFromSeries(BuildDailyCloses(candles, i)) ?? 0m;
        }

        private static decimal? CalcWeeklyMacdLineMinusSignal(List<Candle> candles, int i)
        {
            return CalcMacdLineMinusSignalFromSeries(BuildWeeklyCloses(candles, i));
        }

        private static decimal? CalcMacdLineMinusSignalFromSeries(List<decimal> closes)
        {
            if (closes.Count == 0)
                return null;

            var macdSeries = BuildMacdSeries(closes);
            if (macdSeries.Count == 0)
                return null;

            return macdSeries[^1].Macd - macdSeries[^1].Signal;
        }

        private static decimal? CalcBollingerUpperDistancePctFromSeries(
            List<decimal> closes,
            int length,
            decimal stdDevMultiplier)
        {
            if (closes.Count < length)
                return null;

            var window = closes.Skip(closes.Count - length).ToList();
            var sma = window.Average();
            var stdDev = CalcStandardDeviation(window, sma);
            var upperBand = sma + stdDevMultiplier * stdDev;

            if (upperBand == 0m)
                return null;

            var close = closes[^1];
            return (upperBand - close) / close * 100m;
        }

        private static decimal? CalcBollingerBandWidthPctFromSeries(
            List<decimal> closes,
            int length,
            decimal stdDevMultiplier)
        {
            if (closes.Count < length)
                return null;

            var window = closes.Skip(closes.Count - length).ToList();
            var sma = window.Average();

            if (sma == 0m)
                return null;

            var stdDev = CalcStandardDeviation(window, sma);
            var upperBand = sma + stdDevMultiplier * stdDev;
            var lowerBand = sma - stdDevMultiplier * stdDev;

            return (upperBand - lowerBand) / sma * 100m;
        }

        private static decimal CalcStandardDeviation(List<decimal> values, decimal mean)
        {
            if (values.Count == 0)
                return 0m;

            decimal variance = 0m;

            foreach (var value in values)
            {
                var delta = value - mean;
                variance += delta * delta;
            }

            variance /= values.Count;
            return (decimal)Math.Sqrt((double)variance);
        }

        private static List<decimal> BuildDailyCloses(List<Candle> candles, int i)
        {
            var result = new List<decimal>();
            DateTime? currentDay = null;

            for (int k = 0; k < i; k++)
            {
                var candle = candles[k];
                var day = candle.Time.Date;

                if (currentDay == null || currentDay.Value != day)
                {
                    result.Add(candle.Close);
                    currentDay = day;
                }
                else
                {
                    result[^1] = candle.Close;
                }
            }

            return result;
        }

        private static List<DailyBar> BuildDailyBars(List<Candle> candles, int i)
        {
            var result = new List<DailyBar>();

            DailyBar? current = null;

            for (int k = 0; k < i; k++)
            {
                var candle = candles[k];
                var day = candle.Time.Date;

                if (current == null || current.Day != day)
                {
                    if (current != null)
                        result.Add(current);

                    current = new DailyBar
                    {
                        Day = day,
                        Open = candle.Open,
                        High = candle.High,
                        Low = candle.Low,
                        Close = candle.Close
                    };
                }
                else
                {
                    if (candle.High > current.High)
                        current.High = candle.High;

                    if (candle.Low < current.Low)
                        current.Low = candle.Low;

                    current.Close = candle.Close;
                }
            }

            if (current != null)
                result.Add(current);

            return result;
        }

        private static List<decimal> BuildWeeklyCloses(List<Candle> candles, int i)
        {
            var weeklyBars = BuildWeeklyBars(candles, i);
            var closes = new List<decimal>(weeklyBars.Count);

            for (int k = 0; k < weeklyBars.Count; k++)
                closes.Add(weeklyBars[k].Close);

            return closes;
        }

        private static List<WeeklyBar> BuildWeeklyBars(List<Candle> candles, int i)
        {
            var result = new List<WeeklyBar>();

            WeeklyBar? current = null;

            for (int k = 0; k < i; k++)
            {
                var candle = candles[k];
                var weekStart = GetWeekStart(candle.Time);

                if (current == null || current.WeekStart != weekStart)
                {
                    if (current != null)
                        result.Add(current);

                    current = new WeeklyBar
                    {
                        WeekStart = weekStart,
                        Open = candle.Open,
                        High = candle.High,
                        Low = candle.Low,
                        Close = candle.Close
                    };
                }
                else
                {
                    if (candle.High > current.High)
                        current.High = candle.High;

                    if (candle.Low < current.Low)
                        current.Low = candle.Low;

                    current.Close = candle.Close;
                }
            }

            if (current != null)
                result.Add(current);

            return result;
        }

        private static DateTime GetWeekStart(DateTime time)
        {
            var date = time.Date;
            int diff = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
            return date.AddDays(-diff);
        }

        private static List<MacdPoint> BuildMacdSeries(List<decimal> closes)
        {
            const int fastLength = 12;
            const int slowLength = 26;
            const int signalLength = 9;

            var result = new List<MacdPoint>(closes.Count);

            if (closes.Count == 0)
                return result;

            decimal fastK = 2m / (fastLength + 1);
            decimal slowK = 2m / (slowLength + 1);
            decimal signalK = 2m / (signalLength + 1);

            decimal fastEma = closes[0];
            decimal slowEma = closes[0];
            decimal signal = 0m;
            bool signalInitialized = false;

            for (int i = 0; i < closes.Count; i++)
            {
                var close = closes[i];

                if (i == 0)
                {
                    fastEma = close;
                    slowEma = close;
                }
                else
                {
                    fastEma = close * fastK + fastEma * (1m - fastK);
                    slowEma = close * slowK + slowEma * (1m - slowK);
                }

                var macd = fastEma - slowEma;

                if (!signalInitialized)
                {
                    signal = macd;
                    signalInitialized = true;
                }
                else
                {
                    signal = macd * signalK + signal * (1m - signalK);
                }

                var histogram = macd - signal;

                result.Add(new MacdPoint
                {
                    Macd = macd,
                    Signal = signal,
                    Histogram = histogram
                });
            }

            return result;
        }

        private sealed class DailyBar
        {
            public DateTime Day { get; set; }
            public decimal Open { get; set; }
            public decimal High { get; set; }
            public decimal Low { get; set; }
            public decimal Close { get; set; }
        }

        private sealed class WeeklyBar
        {
            public DateTime WeekStart { get; set; }
            public decimal Open { get; set; }
            public decimal High { get; set; }
            public decimal Low { get; set; }
            public decimal Close { get; set; }
        }

        private sealed class MacdPoint
        {
            public decimal Macd { get; set; }
            public decimal Signal { get; set; }
            public decimal Histogram { get; set; }
        }
    }
}
