using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Analysis
{
    public class FeatureEngine : IFeatureEngine
    {
        public FeatureSet Calculate(List<Candle> candles)
        {
            int i = candles.Count;

            return new FeatureSet
            {
                Pullback10d = CalcPullback(candles, i, 10),
                DistanceTo20dHigh = CalcDistanceTo20dHigh(candles, i),
                DistanceTo52wHigh = CalcDistanceTo52wHigh(candles, i),
                VolumeRatio20 = CalcVolumeRatio(candles, i, 20),
                ATRRatio = CalcATRRatio(candles, i),
                TrendPosition = CalcTrendPosition(candles, i, 50)
            };
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
