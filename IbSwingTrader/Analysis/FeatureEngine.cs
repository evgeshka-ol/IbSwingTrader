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
                MACDHistImproving = macdHistDelta > 0m
            };
        }

        public FeatureSet CalculateLast(List<Candle> candles)
        {
            return Calculate(candles, candles.Count);
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
    }
}
