using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Analysis
{
    public class TradeDatasetBuilder(
        IFeatureEngine featureEngine,
        ICandidateScore candidateScore,
        IFutureStatsCalculator futureStatsCalculator,
        ITextLogger logger) : ITradeDatasetBuilder
    {
        private static readonly int[] EntryShifts = { -12, -9, -6, -3, 0 };

        private readonly IFeatureEngine _featureEngine = featureEngine;
        private readonly ICandidateScore _candidateScore = candidateScore;
        private readonly IFutureStatsCalculator _futureStatsCalculator = futureStatsCalculator;
        private readonly ITextLogger _logger = logger;

        public List<TradeDatasetRow> Build(
            List<TradeRecord> trades,
            List<Candle> candles)
        {
            var rows = new List<TradeDatasetRow>();

            for (int t = 0; t < trades.Count; t++)
            {
                var trade = trades[t];

                var entryIndexReal = FindEntryBarIndex(candles, trade.EntryTimeUtc);

                // вход раньше первой свечи
                if (entryIndexReal < 0)
                {
                    _logger.Info($"Trade {t}: Entry time {trade.EntryTimeUtc} is before first candle {candles[0].Time}");
                    continue;
                }

                // нужен warmup для индикаторов
                if (entryIndexReal < 60)
                {
                    _logger.Info($"Trade {t}: Entry index {entryIndexReal} is too early for indicator warmup");
                    continue;
                }

                if (entryIndexReal >= candles.Count - 20)
                {
                    _logger.Info($"Trade {t}: Entry index {entryIndexReal} is too late, not enough future bars");
                    continue;
                }

                var candlePriceReal = candles[entryIndexReal].Close;
                var splitFactor = DetectSplitFactor(trade.EntryPrice, candlePriceReal);

                foreach (var shift in EntryShifts)
                {
                    int entryIndex = entryIndexReal + shift;

                    if (entryIndex < 0)
                    {
                        _logger.Info($"Trade {t}: Shift {shift} leads to entry index {entryIndex} before first candle");
                        continue;
                    }

                    if (entryIndex < 60)
                    {
                        _logger.Info($"Trade {t}: Shift {shift} leads to entry index {entryIndex} too early for indicator warmup");
                        continue;
                    }

                    if (entryIndex >= candles.Count - 20)
                        continue;

                    var entryTime = shift == 0
                        ? trade.EntryTimeUtc
                        : candles[entryIndex].Time;

                    if (entryTime >= trade.ExitTimeUtc)
                    {
                        _logger.Info($"Trade {t}: Shift {shift} leads to entry time {entryTime} after exit time {trade.ExitTimeUtc}");
                        continue;
                    }

                    if ((trade.ExitTimeUtc - entryTime).TotalHours < 4)
                    {
                        _logger.Info($"Trade {t}: Shift {shift} leads to hold time {(trade.ExitTimeUtc - entryTime).TotalHours} hours, less than 4 hours");
                        continue;
                    }

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

                    CalculateFeatures(row, candles, entryIndex);

                    if (!HasFutureBars(candles, entryIndex))
                    {
                        _logger.Info($"Trade {t}: Shift {shift} leads to entry index {entryIndex} with insufficient future bars");
                        continue;
                    }

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

                if (candles[mid].Time <= entryTime)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            return right;
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

            row.BBPosition = featureSet.BBPosition;
            row.BBPositionCentered = row.BBPosition - 0.5m;

            row.BBMidSignedDistancePct = featureSet.BBMidSignedDistancePct;
            row.IsBelowBBMid = row.BBMidSignedDistancePct < 0m;
            row.DistanceToBBLowerPct = featureSet.DistanceToBBLowerPct;

            row.RSI14 = featureSet.RSI14;
            row.ATRRatio = featureSet.ATRRatio;

            row.MACDHist = featureSet.MACDHist;
            row.MACDHistDelta = featureSet.MACDHistDelta;
            row.MACDHistImproving = row.MACDHistDelta > 0m;

            row.DailyTrendPosition = CalcDailyTrendPosition(candles, i);
            row.DailyPullback10d = CalcDailyPullback10d(candles, i);
            row.DailyRSI14 = CalcDailyRSI14(candles, i);

            row.WeeklyTrendPosition = CalcWeeklyTrendPosition(candles, i);
            row.WeeklyBBMidSlopePct = CalcWeeklyBBMidSlopePct(candles, i);
            row.WeeklyMACDHistDelta = CalcWeeklyMACDHistDelta(candles, i);
            row.WeeklyMACDLineMinusSignal = CalcWeeklyMACDLineMinusSignal(candles, i);

            row.DistanceTo20dHigh = featureSet.DistanceTo20dHigh;
            row.DistanceTo52wHigh = featureSet.DistanceTo52wHigh;

            row.CandidateScore = _candidateScore.Calculate(featureSet);
        }

        private static bool HasFutureBars(
            List<Candle> candles,
            int i)
        {
            const int futureBars = 12;

            return i + futureBars < candles.Count;
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
    }
}