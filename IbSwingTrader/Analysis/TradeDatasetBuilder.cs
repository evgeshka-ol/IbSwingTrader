using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Analysis
{
    public class TradeDatasetBuilder : ITradeDatasetBuilder
    {
        public List<TradeDatasetRow> Build(
            List<TradeRecord> trades,
            List<Candle> candles)
        {
            var rows = new List<TradeDatasetRow>();

            for (int t = 0; t < trades.Count; t++)
            {
                var trade = trades[t];

                var entryIndexReal = FindEntryBarIndex(candles, trade.EntryTimeUtc);

                if (entryIndexReal < 50)
                    continue;

                if (entryIndexReal >= candles.Count - 12)
                    continue;

                int[] shifts = { -3, -2, -1, 0, 1, 2, 3 };

                var candlePriceReal = candles[entryIndexReal].Close;
                var splitFactor = DetectSplitFactor(trade.EntryPrice, candlePriceReal);

                foreach (var shift in shifts)
                {
                    int entryIndex = entryIndexReal + shift;

                    if (entryIndex < 50)
                        continue;

                    if (entryIndex >= candles.Count - 12)
                        continue;

                    var entryTime = candles[entryIndex].Time;

                    if (entryTime.Date >= trade.ExitTimeUtc.Date)
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
                    CalculateFuture(row, candles, entryIndex);

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

        private static void CalculateFuture(
            TradeDatasetRow row,
            List<Candle> candles,
            int i)
        {
            var entry = row.EntryPrice;

            var high1 = decimal.MinValue;
            var high2 = decimal.MinValue;

            var low1 = decimal.MaxValue;
            var low2 = decimal.MaxValue;

            // 1 день = 6 баров
            for (int k = 1; k <= 6; k++)
            {
                var c = candles[i + k];

                high1 = Math.Max(high1, c.High);
                low1 = Math.Min(low1, c.Low);
            }

            // 2 дня = 12 баров
            for (int k = 1; k <= 12; k++)
            {
                var c = candles[i + k];

                high2 = Math.Max(high2, c.High);
                low2 = Math.Min(low2, c.Low);
            }

            row.FutureHigh1d = high1;
            row.FutureLow1d = low1;

            row.FutureHigh2d = high2;
            row.FutureLow2d = low2;

            row.MaxReturn1d = (high1 - entry) / entry;
            row.MaxReturn2d = (high2 - entry) / entry;

            row.MaxDrawdown1d = (low1 - entry) / entry;
            row.MaxDrawdown2d = (low2 - entry) / entry;

            row.Target10pct1d = high1 >= entry * 1.10m;
            row.Target10pct2d = high2 >= entry * 1.10m;
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
