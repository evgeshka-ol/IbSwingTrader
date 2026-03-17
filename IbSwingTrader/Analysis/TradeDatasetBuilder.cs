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
            row.BBPositionCentered = featureSet.BBPositionCentered;

            row.BBMidSignedDistancePct = featureSet.BBMidSignedDistancePct;
            row.IsBelowBBMid = featureSet.IsBelowBBMid;
            row.DistanceToBBLowerPct = featureSet.DistanceToBBLowerPct;

            row.RSI14 = featureSet.RSI14;
            row.ATRRatio = featureSet.ATRRatio;

            row.MACDHist = featureSet.MACDHist;
            row.MACDHistDelta = featureSet.MACDHistDelta;
            row.MACDHistImproving = featureSet.MACDHistImproving;

            row.DailyTrendPosition = featureSet.DailyTrendPosition;
            row.DailyPullback10d = featureSet.DailyPullback10d;
            row.DailyRSI14 = featureSet.DailyRSI14;

            row.WeeklyTrendPosition = featureSet.WeeklyTrendPosition;
            row.WeeklyBBMidSlopePct = featureSet.WeeklyBBMidSlopePct;
            row.WeeklyMACDHistDelta = featureSet.WeeklyMACDHistDelta;
            row.WeeklyMACDLineMinusSignal = featureSet.WeeklyMACDLineMinusSignal;

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
    }
}