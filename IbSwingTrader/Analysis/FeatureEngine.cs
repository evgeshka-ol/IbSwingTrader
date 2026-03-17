using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Analysis
{
    public class FeatureEngine : IFeatureEngine
    {
        public FeatureSet Calculate(List<Candle> candles, int index)
        {
            if (index <= 0 || index > candles.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var bbPosition = CalcBBPosition(candles, index);
            var bbMidSignedDistancePct = CalcBBMidSignedDistancePct(candles, index);
            var macdHist = CalcMACDHist(candles, index);
            var macdHistDelta = CalcMACDHistDelta(candles, index);
            var rsi14 = CalcRSI(candles, index);

            return new FeatureSet
            {
                // --- 4H ---
                Pullback5d = CalcPullback(candles, index, 5),
                Pullback10d = CalcPullback(candles, index, 10),
                DistanceTo20dHigh = CalcDistanceTo20dHigh(candles, index),
                DistanceTo52wHigh = CalcDistanceTo52wHigh(candles, index),
                VolumeRatio20 = CalcVolumeRatio(candles, index, 20),
                ATRRatio = CalcATRRatio(candles, index),
                TrendPosition = CalcTrendPosition(candles, index, 50),

                BBPosition = bbPosition,
                BBPositionCentered = bbPosition - 0.5m,
                BBMidSignedDistancePct = bbMidSignedDistancePct,
                IsBelowBBMid = bbMidSignedDistancePct < 0m,
                DistanceToBBLowerPct = CalcDistanceToBBLowerPct(candles, index),

                RSI14 = rsi14,
                MACDHist = macdHist,
                MACDHistDelta = macdHistDelta,
                MACDHistImproving = macdHistDelta > 0m,

                // --- daily ---
                DailyTrendPosition = CalcDailyTrendPosition(candles, index),
                DailyPullback10d = CalcDailyPullback10d(candles, index),
                DailyRSI14 = CalcDailyRSI14(candles, index),

                // --- weekly ---
                WeeklyTrendPosition = CalcWeeklyTrendPosition(candles, index),
                WeeklyBBMidSlopePct = CalcWeeklyBBMidSlopePct(candles, index),
                WeeklyMACDHistDelta = CalcWeeklyMACDHistDelta(candles, index),
                WeeklyMACDLineMinusSignal = CalcWeeklyMACDLineMinusSignal(candles, index)
            };
        }

        private static decimal? CalcWeeklyTrendPosition(List<Candle> candles, int i)
        {
            const int length = 20;

            if (i <= 0 || i > candles.Count)
                return null;

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

            return close / sma;
        }

        private static decimal? CalcWeeklyBBMidSlopePct(
            List<Candle> candles,
            int i,
            int length = 20,
            int lookbackBars = 1)
        {
            if (i <= 0 || i > candles.Count)
                return null;

            var weeklyCloses = BuildWeeklyCloses(candles, i);

            if (weeklyCloses.Count < length + lookbackBars)
                return null;

            decimal currentSum = 0m;
            for (int k = weeklyCloses.Count - length; k < weeklyCloses.Count; k++)
                currentSum += weeklyCloses[k];

            var currentMid = currentSum / length;

            int prevEndExclusive = weeklyCloses.Count - lookbackBars;
            int prevStart = prevEndExclusive - length;

            decimal prevSum = 0m;
            for (int k = prevStart; k < prevEndExclusive; k++)
                prevSum += weeklyCloses[k];

            var prevMid = prevSum / length;

            if (prevMid == 0m)
                return null;

            return (currentMid - prevMid) / prevMid * 100m;
        }

        private static decimal? CalcWeeklyMACDHistDelta(List<Candle> candles, int i)
        {
            if (i <= 0 || i > candles.Count)
                return null;

            var weeklyCloses = BuildWeeklyCloses(candles, i);

            if (weeklyCloses.Count < 2)
                return null;

            var macdSeries = BuildMacdSeries(weeklyCloses);

            if (macdSeries.Count < 2)
                return null;

            return macdSeries[^1].Histogram - macdSeries[^2].Histogram;
        }

        private static decimal? CalcWeeklyMACDLineMinusSignal(List<Candle> candles, int i)
        {
            if (i <= 0 || i > candles.Count)
                return null;

            var weeklyCloses = BuildWeeklyCloses(candles, i);

            if (weeklyCloses.Count == 0)
                return null;

            var macdSeries = BuildMacdSeries(weeklyCloses);

            if (macdSeries.Count == 0)
                return null;

            return macdSeries[^1].Macd - macdSeries[^1].Signal;
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

        public FeatureSet CalculateLast(List<Candle> candles)
        {
            return Calculate(candles, candles.Count);
        }

        private static decimal CalcDailyRSI14(List<Candle> candles, int i)
        {
            const int rsiLength = 14;

            if (i <= 0 || i > candles.Count)
                return 0m;

            var dailyCloses = new List<decimal>();
            DateTime? currentDay = null;

            for (int k = 0; k < i; k++)
            {
                var candle = candles[k];
                var day = candle.Time.Date;

                if (currentDay == null || currentDay.Value != day)
                {
                    dailyCloses.Add(candle.Close);
                    currentDay = day;
                }
                else
                {
                    dailyCloses[^1] = candle.Close;
                }
            }

            if (dailyCloses.Count < rsiLength + 1)
                return 0m;

            decimal gainSum = 0m;
            decimal lossSum = 0m;

            int start = dailyCloses.Count - rsiLength;

            for (int k = start; k < dailyCloses.Count; k++)
            {
                var change = dailyCloses[k] - dailyCloses[k - 1];

                if (change > 0)
                    gainSum += change;
                else
                    lossSum -= change;
            }

            var avgGain = gainSum / rsiLength;
            var avgLoss = lossSum / rsiLength;

            if (avgLoss == 0m)
                return 100m;

            var rs = avgGain / avgLoss;

            return 100m - (100m / (1m + rs));
        }

        private static decimal CalcDailyPullback10d(List<Candle> candles, int i)
        {
            const int days = 10;

            if (i <= 0 || i > candles.Count)
                return 0m;

            var dailyBars = BuildDailyBars(candles, i);

            if (dailyBars.Count == 0)
                return 0m;

            int start = Math.Max(0, dailyBars.Count - days);

            decimal highest = decimal.MinValue;

            for (int k = start; k < dailyBars.Count; k++)
            {
                var high = dailyBars[k].High;

                if (high > highest)
                    highest = high;
            }

            if (highest <= 0m)
                return 0m;

            var close = dailyBars[^1].Close;

            return (close - highest) / highest * 100m;
        }

        private static List<(DateTime Day, decimal High, decimal Close)> BuildDailyBars(List<Candle> candles, int i)
        {
            var result = new List<(DateTime Day, decimal High, decimal Close)>();

            DateTime? currentDay = null;
            decimal dayHigh = 0m;
            decimal dayClose = 0m;

            for (int k = 0; k < i; k++)
            {
                var candle = candles[k];
                var day = candle.Time.Date;

                if (currentDay == null || currentDay.Value != day)
                {
                    if (currentDay != null)
                        result.Add((currentDay.Value, dayHigh, dayClose));

                    currentDay = day;
                    dayHigh = candle.High;
                    dayClose = candle.Close;
                }
                else
                {
                    if (candle.High > dayHigh)
                        dayHigh = candle.High;

                    dayClose = candle.Close;
                }
            }

            if (currentDay != null)
                result.Add((currentDay.Value, dayHigh, dayClose));

            return result;
        }

        private static decimal CalcDailyTrendPosition(List<Candle> candles, int i)
        {
            const int length = 50;

            if (i <= 0 || i > candles.Count)
                return 1m;

            var dailyCloses = BuildDailyCloses(candles, i);

            if (dailyCloses.Count < length)
                return 1m;

            decimal sum = 0m;

            for (int k = dailyCloses.Count - length; k < dailyCloses.Count; k++)
                sum += dailyCloses[k];

            var sma = sum / length;

            if (sma == 0m)
                return 1m;

            var close = dailyCloses[^1];

            return close / sma;
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

        private static decimal CalcBBMidSignedDistancePct(List<Candle> candles, int i, int length = 20, decimal stdDevMult = 2m)
        {
            if (i < length)
                return 0m;

            decimal sum = 0m;

            for (int k = i - length; k < i; k++)
                sum += candles[k].Close;

            var middle = sum / length;

            if (middle == 0m)
                return 0m;

            var close = candles[i - 1].Close;

            return (close - middle) / middle * 100m;
        }

        private static decimal CalcBBPosition(List<Candle> candles, int i)
        {
            const int length = 20;

            decimal sum = 0;

            for (int k = i - length; k < i; k++)
                sum += candles[k].Close;

            var sma = sum / length;

            decimal variance = 0;

            for (int k = i - length; k < i; k++)
            {
                var diff = candles[k].Close - sma;
                variance += diff * diff;
            }

            var std = (decimal)Math.Sqrt((double)(variance / length));

            var upper = sma + 2 * std;
            var lower = sma - 2 * std;

            var close = candles[i - 1].Close;

            if (upper == lower)
                return 0.5m;

            return (close - lower) / (upper - lower);
        }

        private static decimal CalcRSI(List<Candle> candles, int i)
        {
            const int length = 14;

            decimal gain = 0;
            decimal loss = 0;

            for (int k = i - length; k < i; k++)
            {
                var diff = candles[k].Close - candles[k - 1].Close;

                if (diff > 0)
                    gain += diff;
                else
                    loss -= diff;
            }

            if (loss == 0)
                return 100;

            var rs = gain / loss;

            return 100 - (100 / (1 + rs));
        }

        private static decimal CalcDistanceToBBLowerPct(List<Candle> candles, int i, int length = 20, decimal stdDevMult = 2m)
        {
            if (i < length)
                return 0m;

            decimal sum = 0m;

            for (int k = i - length; k < i; k++)
                sum += candles[k].Close;

            var middle = sum / length;

            decimal varianceSum = 0m;

            for (int k = i - length; k < i; k++)
            {
                var diff = candles[k].Close - middle;
                varianceSum += diff * diff;
            }

            var variance = varianceSum / length;
            var stdDev = (decimal)Math.Sqrt((double)variance);

            var lower = middle - stdDevMult * stdDev;

            if (lower == 0m)
                return 0m;

            var close = candles[i - 1].Close;

            return (close - lower) / lower * 100m;
        }

        private static decimal CalcMACDHistDelta(List<Candle> candles, int i)
        {
            if (i < 2)
                return 0m;

            var current = CalcMACDHist(candles, i);
            var previous = CalcMACDHist(candles, i - 1);

            return current - previous;
        }

        private static decimal CalcMACDHist(List<Candle> candles, int i)
        {
            const int fast = 12;
            const int slow = 26;
            const int signalLen = 9;

            decimal multiplierFast = 2m / (fast + 1);
            decimal multiplierSlow = 2m / (slow + 1);

            decimal emaFast = candles[i - fast].Close;
            decimal emaSlow = candles[i - slow].Close;

            List<decimal> macdSeries = new();

            for (int k = i - slow + 1; k < i; k++)
            {
                emaFast = ((candles[k].Close - emaFast) * multiplierFast) + emaFast;
                emaSlow = ((candles[k].Close - emaSlow) * multiplierSlow) + emaSlow;

                macdSeries.Add(emaFast - emaSlow);
            }

            decimal multiplierSignal = 2m / (signalLen + 1);
            decimal signal = macdSeries[0];

            for (int k = 1; k < macdSeries.Count; k++)
                signal = ((macdSeries[k] - signal) * multiplierSignal) + signal;

            var macd = macdSeries[^1];

            return macd - signal;
        }

        private static decimal CalcPullback(List<Candle> candles, int i, int days)
        {
            int bars = days * 6;

            int start = i - bars;
            if (start < 0)
                start = 0;

            decimal highest = decimal.MinValue;

            for (int k = start; k < i; k++)
            {
                var h = candles[k].High;

                if (h > highest)
                    highest = h;
            }

            if (highest <= 0)
                return 0;

            var close = candles[i - 1].Close;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalcVolumeRatio(List<Candle> candles, int i, int length)
        {
            int start = i - length;

            if (start < 0)
                return 1;

            decimal sum = 0;

            for (int k = start; k < i; k++)
                sum += candles[k].Volume;

            if (sum == 0)
                return 1;

            var avg = sum / length;

            return candles[i - 1].Volume / avg;
        }

        private static decimal CalcDistanceTo20dHigh(List<Candle> candles, int i)
        {
            int bars = 20 * 6;

            int start = i - bars;
            if (start < 0)
                start = 0;

            decimal highest = decimal.MinValue;

            for (int k = start; k < i; k++)
            {
                var h = candles[k].High;

                if (h > highest)
                    highest = h;
            }

            if (highest <= 0)
                return 0;

            var close = candles[i - 1].Close;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalcDistanceTo52wHigh(List<Candle> candles, int i)
        {
            int bars = 252 * 6;

            int start = i - bars;
            if (start < 0)
                start = 0;

            decimal highest = decimal.MinValue;

            for (int k = start; k < i; k++)
            {
                var h = candles[k].High;

                if (h > highest)
                    highest = h;
            }

            if (highest <= 0)
                return 0;

            var close = candles[i - 1].Close;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalcATRRatio(List<Candle> candles, int i)
        {
            const int length = 14;

            if (i < length + 1)
                return 0;

            decimal atr = 0;

            for (int k = i - length; k < i; k++)
            {
                var high = candles[k].High;
                var low = candles[k].Low;
                var prevClose = candles[k - 1].Close;

                var tr1 = high - low;
                var tr2 = Math.Abs(high - prevClose);
                var tr3 = Math.Abs(low - prevClose);

                var tr = Math.Max(tr1, Math.Max(tr2, tr3));

                atr += tr;
            }

            atr /= length;

            var close = candles[i - 1].Close;

            if (close == 0)
                return 0;

            return atr / close;
        }

        private static decimal CalcTrendPosition(List<Candle> candles, int i, int length)
        {
            int start = i - length;

            if (start < 0)
                return 1;

            decimal sum = 0;

            for (int k = start; k < i; k++)
                sum += candles[k].Close;

            var ma = sum / length;

            if (ma == 0)
                return 1;

            return candles[i - 1].Close / ma;
        }

        private static List<MacdPoint> BuildMacdSeries(
            List<decimal> closes,
            int fast = 12,
            int slow = 26,
            int signalLen = 9)
        {
            var result = new List<MacdPoint>();

            if (closes.Count == 0)
                return result;

            decimal fastMultiplier = 2m / (fast + 1);
            decimal slowMultiplier = 2m / (slow + 1);
            decimal signalMultiplier = 2m / (signalLen + 1);

            decimal emaFast = closes[0];
            decimal emaSlow = closes[0];
            decimal signal = 0m;
            bool signalInitialized = false;

            for (int k = 0; k < closes.Count; k++)
            {
                var close = closes[k];

                if (k == 0)
                {
                    emaFast = close;
                    emaSlow = close;
                }
                else
                {
                    emaFast = ((close - emaFast) * fastMultiplier) + emaFast;
                    emaSlow = ((close - emaSlow) * slowMultiplier) + emaSlow;
                }

                var macd = emaFast - emaSlow;

                if (!signalInitialized)
                {
                    signal = macd;
                    signalInitialized = true;
                }
                else
                {
                    signal = ((macd - signal) * signalMultiplier) + signal;
                }

                result.Add(new MacdPoint
                {
                    Macd = macd,
                    Signal = signal
                });
            }

            return result;
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
            public decimal Histogram => Macd - Signal;
        }
    }
}
