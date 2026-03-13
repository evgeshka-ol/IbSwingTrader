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
                if (entryIndexReal < 30)
                    continue;

                // нужен хотя бы 1 будущий бар
                if (entryIndexReal >= candles.Count - 2)
                    continue;

                var candlePriceReal = candles[entryIndexReal].Close;
                var splitFactor = DetectSplitFactor(trade.EntryPrice, candlePriceReal);

                foreach (var shift in EntryShifts)
                {
                    int entryIndex = entryIndexReal + shift;

                    if (entryIndex < 30)
                        continue;

                    if (entryIndex >= candles.Count - 2)
                        continue;

                    var entryTime = candles[entryIndex].Time;

                    // synthetic entry не должен быть после exit
                    if (entryTime >= trade.ExitTimeUtc)
                        continue;

                    // synthetic entry не должен быть слишком близко к exit
                    if ((trade.ExitTimeUtc - entryTime).TotalHours < 4)
                        continue;

                    decimal entryPrice = shift == 0
                        ? trade.EntryPrice / splitFactor
                        : candles[entryIndex].Close;

                    decimal exitPrice = trade.ExitPrice / splitFactor;

                    var row = new TradeDatasetRow
                    {
                        Ticker = trade.Ticker,

                        EntryTimeUtc = entryTime,
                        EntryPrice = entryPrice,

                        ExitTimeUtc = trade.ExitTimeUtc,
                        ExitPrice = exitPrice,

                        ProfitPercent =
                            (exitPrice - entryPrice) / entryPrice * 100m,

                        HoldDays = (trade.ExitTimeUtc.Date - entryTime.Date).Days,

                        IsRealTrade = shift == 0,
                        EntryShiftBars = shift
                    };

                    CalculateFeatures(row, candles, entryIndex);
                    if (!HasFutureBars(candles, entryIndex))
                        continue;

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
                {
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
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
        }

        private static bool HasFutureBars(
            List<Candle> candles,
            int i)
        {
            const int futureBars = 12;

            return i + futureBars < candles.Count;
        }

        private static decimal CalcPullback(List<Candle> candles, int i, int days)
        {
            int bars = days * 6;

            int start = Math.Max(0, i - bars);

            decimal highest = decimal.MinValue;

            for (int k = start; k < i; k++)
            {
                highest = Math.Max(highest, candles[k].High);
            }

            var close = candles[i - 1].Close;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalcVolumeRatio(List<Candle> candles, int i, int length)
        {
            decimal sum = 0;

            for (int k = i - length; k < i; k++)
                sum += candles[k].Volume;

            var avg = sum / length;

            return candles[i - 1].Volume / avg;
        }

        private static decimal CalcTrendPosition(List<Candle> candles, int i, int length)
        {
            decimal sum = 0;

            for (int k = i - length; k < i; k++)
                sum += candles[k].Close;

            var ema = sum / length;

            return candles[i - 1].Close / ema;
        }
    }
}
