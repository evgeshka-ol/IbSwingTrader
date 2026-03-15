using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Analysis
{
    public class FutureStatsCalculator : IFutureStatsCalculator
    {
        public void Calculate(TradeDatasetRow row, List<Candle> candles, int entryIndex)
        {
            CalculateFutureStats(row, candles, entryIndex);
        }

        private static void CalculateFutureStats(TradeDatasetRow row, List<Candle> candles, int entryIndex)
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
    }
}
