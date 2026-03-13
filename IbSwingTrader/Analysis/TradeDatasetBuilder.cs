using System.Diagnostics;
using System.Drawing;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Analysis
{
    public class TradeDatasetBuilder : ITradeDatasetBuilder
    {
        private static readonly int[] EntryShifts = { -12, -9, -6, -3, 0 };

        public List<TradeDatasetRow> Build(
            List<TradeRecord> trades,
            List<Candle> candles)
        {
            var rows = new List<TradeDatasetRow>();

            for (int t = 0; t < trades.Count; t++)
            {
                var trade = trades[t];

                var entryIndexReal = FindEntryBarIndex(candles, trade.EntryTimeUtc);

                // нужен warmup для индикаторов
                if (entryIndexReal < 60)
                    continue;

                if (entryIndexReal >= candles.Count - 20)
                    continue;

                var candlePriceReal = candles[entryIndexReal].Close;
                var splitFactor = DetectSplitFactor(trade.EntryPrice, candlePriceReal);

                foreach (var shift in EntryShifts)
                {
                    int entryIndex = entryIndexReal + shift;

                    if (entryIndex < 60)
                        continue;

                    if (entryIndex >= candles.Count - 20)
                        continue;

                    var entryTime = candles[entryIndex].Time;

                    if (entryTime >= trade.ExitTimeUtc)
                        continue;

                    if ((trade.ExitTimeUtc - entryTime).TotalHours < 4)
                        continue;

                    decimal entryPrice = shift == 0
                        ? trade.EntryPrice / splitFactor
                        : candles[entryIndex].Close;

                    decimal exitPrice = trade.ExitPrice / splitFactor;
                    decimal side = trade.IsShort ? -1m : 1m;

                    var row = new TradeDatasetRow
                    {
                        Ticker = trade.Ticker,

                        EntryTimeUtc = entryTime,
                        EntryPrice = entryPrice,

                        ExitTimeUtc = trade.ExitTimeUtc,
                        ExitPrice = exitPrice,

                        ProfitPercent = side * (exitPrice - entryPrice) / entryPrice * 100m,

                        HoldDays = (trade.ExitTimeUtc.Date - entryTime.Date).Days,

                        IsRealTrade = shift == 0,
                        EntryShiftBars = shift
                    };

                    // FEATURES
                    CalculateFeatures(row, candles, entryIndex);

                    // TARGET требует future
                    if (!HasFutureBars(candles, entryIndex))
                        continue;
                    CalculateFutureStats(row, candles, entryIndex);

                    rows.Add(row);
                }
            }

            return rows;
        }

        private static decimal DetectSplitFactor(decimal tradePrice, decimal candlePrice)
        {
            if (candlePrice <= 0)
                return 1m;

            var ratio = tradePrice / candlePrice;
            var rounded = Math.Round(ratio);

            if (rounded >= 2 && rounded <= 20 &&
                Math.Abs(ratio - rounded) < 0.2m)
                return rounded;

            return 1m;
        }

        private static int FindEntryBarIndex(List<Candle> candles, DateTime entryTime)
        {
            int left = 0;
            int right = candles.Count - 1;

            while (left <= right)
            {
                int mid = left + ((right - left) >> 1);

                if (candles[mid].Time < entryTime)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            return left;
        }

        private static void CalculateFeatures(
            TradeDatasetRow row,
            List<Candle> candles,
            int i)
        {
            row.Pullback5d = CalcPullback(candles, i, 5);
            row.Pullback10d = CalcPullback(candles, i, 10);

            row.VolumeRatio20 = CalcVolumeRatio(candles, i, 20);
            row.TrendPosition = CalcTrendPosition(candles, i, 50);

            row.BBPosition = CalcBBPosition(candles, i);
            row.RSI14 = CalcRSI(candles, i);
            row.ATRRatio = CalcATRRatio(candles, i);
            row.MACDHist = CalcMACDHist(candles, i);

            row.DailyTrendPosition = CalcDailyTrendPosition(candles, i);
            row.DailyPullback10d = CalcDailyPullback10d(candles, i);
            row.DailyRSI14 = CalcRSI(candles, i);

            row.WeeklyTrendPosition = CalcWeeklyTrendPosition(candles, i);

            // новые признаки
            row.DistanceTo20dHigh = CalcDistanceTo20dHigh(candles, i);
            row.DistanceTo52wHigh = CalcDistanceTo52wHigh(candles, i);

            // scoring
            row.CandidateScore = CalcCandidateScore(row);
        }

        private static bool HasFutureBars(
            List<Candle> candles,
            int i)
        {
            const int futureBars = 12;

            return i + futureBars < candles.Count;
        }

        private static void CalculateFutureStats(
            TradeDatasetRow row,
            List<Candle> candles,
            int entryIndex)
        {
            const int barsPerDay = 6;

            int end1d = entryIndex + barsPerDay;
            int end2d = entryIndex + barsPerDay * 2;

            decimal high1d = decimal.MinValue;
            decimal low1d = decimal.MaxValue;

            decimal high2d = decimal.MinValue;
            decimal low2d = decimal.MaxValue;

            for (int k = entryIndex + 1; k <= end2d; k++)
            {
                var c = candles[k];

                if (k <= end1d)
                {
                    if (c.High > high1d) high1d = c.High;
                    if (c.Low < low1d) low1d = c.Low;
                }

                if (c.High > high2d) high2d = c.High;
                if (c.Low < low2d) low2d = c.Low;
            }

            row.FutureHigh1d = high1d;
            row.FutureLow1d = low1d;

            row.FutureHigh2d = high2d;
            row.FutureLow2d = low2d;

            decimal maxRet1;
            decimal maxRet2;
            decimal dd1;
            decimal dd2;

            if (row.IsShort)
            {
                maxRet1 = (row.EntryPrice - low1d) / row.EntryPrice * 100m;
                maxRet2 = (row.EntryPrice - low2d) / row.EntryPrice * 100m;

                dd1 = (row.EntryPrice - high1d) / row.EntryPrice * 100m;
                dd2 = (row.EntryPrice - high2d) / row.EntryPrice * 100m;
            }
            else
            {
                maxRet1 = (high1d - row.EntryPrice) / row.EntryPrice * 100m;
                maxRet2 = (high2d - row.EntryPrice) / row.EntryPrice * 100m;

                dd1 = (low1d - row.EntryPrice) / row.EntryPrice * 100m;
                dd2 = (low2d - row.EntryPrice) / row.EntryPrice * 100m;
            }

            // защита от bad candles / splits
            maxRet1 = Math.Clamp(maxRet1, -100m, 300m);
            maxRet2 = Math.Clamp(maxRet2, -100m, 300m);
            dd1 = Math.Clamp(dd1, -100m, 100m);
            dd2 = Math.Clamp(dd2, -100m, 100m);

            row.MaxReturn1d = maxRet1;
            row.MaxReturn2d = maxRet2;

            row.MaxDrawdown1d = dd1;
            row.MaxDrawdown2d = dd2;

            // targets
            row.Target10pct1d = row.MaxReturn1d >= 10m;
            row.Target10pct2d = row.MaxReturn2d >= 10m;
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

        private static decimal CalcDailyTrendPosition(List<Candle> candles, int i)
        {
            const int length = 50;

            decimal sum = 0;

            for (int k = i - length; k < i; k++)
                sum += candles[k].Close;

            var sma = sum / length;

            return candles[i - 1].Close / sma;
        }

        private static decimal CalcDailyPullback10d(List<Candle> candles, int i)
        {
            int bars = 10 * 6;

            int start = Math.Max(0, i - bars);

            decimal highest = decimal.MinValue;

            for (int k = start; k < i; k++)
                highest = Math.Max(highest, candles[k].High);

            var close = candles[i - 1].Close;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalcWeeklyTrendPosition(List<Candle> candles, int i)
        {
            int bars = 5 * 6 * 4;

            int start = Math.Max(0, i - bars);

            decimal sum = 0;
            int count = 0;

            for (int k = start; k < i; k++)
            {
                sum += candles[k].Close;
                count++;
            }

            var sma = sum / count;

            return candles[i - 1].Close / sma;
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

        private static decimal CalcCandidateScore(TradeDatasetRow row)
        {
            decimal score =
                (-row.DistanceTo20dHigh * 0.35m)
                + (-row.Pullback10d * 0.25m)
                + (row.VolumeRatio20 * 0.15m)
                + ((0.08m - row.ATRRatio) * 100 * 0.15m)
                + ((1 - row.TrendPosition) * 100 * 0.1m);

            return score;
        }
    }
}