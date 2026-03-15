using System.Diagnostics;
using System.Drawing;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Analysis
{
    public class TradeDatasetBuilder(
        IFeatureEngine featureEngine,
        ICandidateScore candidateScore,
        IFutureStatsCalculator futureStatsCalculator) : ITradeDatasetBuilder
    {
        private static readonly int[] EntryShifts = { -12, -9, -6, -3, 0 };

        private readonly IFeatureEngine _featureEngine = featureEngine;
        private readonly ICandidateScore _candidateScore = candidateScore;
        private readonly IFutureStatsCalculator _futureStatsCalculator = futureStatsCalculator;

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
                    _futureStatsCalculator.Calculate(row, candles, entryIndex);

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

        private void CalculateFeatures(
            TradeDatasetRow row,
            List<Candle> candles,
            int i)
        {
            var featureSet = _featureEngine.Calculate(candles, i);

            row.Pullback5d = featureSet.Pullback5d;
            row.Pullback10d = featureSet.Pullback10d;

            row.VolumeRatio20 = featureSet.VolumeRatio20;
            row.TrendPosition = featureSet.TrendPosition;

            row.BBPosition = CalcBBPosition(candles, i);
            row.RSI14 = CalcRSI(candles, i);
            row.ATRRatio = featureSet.ATRRatio;
            row.MACDHist = CalcMACDHist(candles, i);

            row.DailyTrendPosition = CalcDailyTrendPosition(candles, i);
            row.DailyPullback10d = CalcDailyPullback10d(candles, i);
            row.DailyRSI14 = CalcRSI(candles, i);

            row.WeeklyTrendPosition = CalcWeeklyTrendPosition(candles, i);

            // новые признаки
            row.DistanceTo20dHigh = featureSet.DistanceTo20dHigh;
            row.DistanceTo52wHigh = featureSet.DistanceTo52wHigh;

            // scoring
            row.CandidateScore = _candidateScore.Calculate(featureSet);
        }

        private static bool HasFutureBars(
            List<Candle> candles,
            int i)
        {
            const int futureBars = 12;

            return i + futureBars < candles.Count;
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
    }
}