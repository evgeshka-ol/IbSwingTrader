using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Analysis
{
    public class TradeDatasetBuilder(
        IFeatureEngine featureEngine,
        ITextLogger logger,
        INumberTextFormatter numberFormatter,
        IBuildDatasetSettingsProvider settingsProvider) : ITradeDatasetBuilder
    {
        private readonly IFeatureEngine _featureEngine = featureEngine;
        private readonly ITextLogger _logger = logger;
        private readonly INumberTextFormatter _fmt = numberFormatter;
        private readonly IBuildDatasetSettingsProvider _settingsProvider = settingsProvider;

        public List<TradeDatasetRow> Build(
            List<TradeRecord> trades,
            List<Candle> candles)
        {
            var rows = new List<TradeDatasetRow>();

            if (candles.Count == 0)
                return rows;

            var entryShifts = _settingsProvider.Get().EntryShifts;

            for (int t = 0; t < trades.Count; t++)
            {
                var trade = trades[t];

                var entryIndexReal = FindBarIndex(candles, trade.EntryTimeUtc);
                var exitIndexReal = FindBarIndex(candles, trade.ExitTimeUtc);

                if (entryIndexReal < 0)
                {
                    _logger.Info($"Trade {t}: Entry time {trade.EntryTimeUtc} is before first candle {candles[0].Time}");
                    continue;
                }

                if (exitIndexReal < 0)
                {
                    _logger.Info($"Trade {t}: Exit time {trade.ExitTimeUtc} is before first candle {candles[0].Time}");
                    continue;
                }

                if (exitIndexReal <= entryIndexReal)
                {
                    _logger.Info($"Trade {t}: Exit index {exitIndexReal} is not after entry index {entryIndexReal}");
                    continue;
                }

                var candlePriceReal = candles[entryIndexReal].Close;
                var splitFactor = DetectSplitFactor(trade.EntryPrice, candlePriceReal);

                foreach (var shift in entryShifts)
                {
                    int entryIndex = entryIndexReal + shift;
                    int exitIndex = exitIndexReal;

                    if (entryIndex < 0)
                    {
                        _logger.Info($"Trade {t}: Shift {shift} leads to entry index {entryIndex} before first candle");
                        continue;
                    }

                    if (entryIndex >= candles.Count)
                    {
                        _logger.Info($"Trade {t}: Shift {shift} leads to entry index {entryIndex} after last candle");
                        continue;
                    }

                    if (entryIndex >= exitIndex)
                    {
                        _logger.Info($"Trade {t}: Shift {shift} leads to entry index {entryIndex} not before exit index {exitIndex}");
                        continue;
                    }

                    if (entryIndex < 60)
                    {
                        _logger.Info($"Trade {t}: Shift {shift} leads to entry index {entryIndex} too early for indicator warmup");
                        continue;
                    }

                    if (exitIndex < 60)
                    {
                        _logger.Info($"Trade {t}: Exit index {exitIndex} is too early for indicator warmup");
                        continue;
                    }

                    var entryTime = shift == 0
                        ? trade.EntryTimeUtc
                        : candles[entryIndex].Time;

                    var exitTime = trade.ExitTimeUtc;

                    if (entryTime >= exitTime)
                    {
                        _logger.Info($"Trade {t}: Shift {shift} leads to entry time {entryTime} after exit time {exitTime}");
                        continue;
                    }

                    var holdHours = (decimal)(exitTime - entryTime).TotalHours;
                    if (holdHours < 4m)
                    {
                        _logger.Info(
                            $"Trade {t}: Shift {shift} leads to hold time {_fmt.Hours(holdHours)} hours, less than 4 hours");
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
                        IsShort = trade.IsShort,

                        EntryTimeUtc = entryTime,
                        EntryPrice = entryPrice,

                        ExitTimeUtc = exitTime,
                        ExitPrice = exitPrice,

                        ProfitPercent = side * (exitPrice - entryPrice) / entryPrice * 100m,
                        HoldDays = (exitTime.Date - entryTime.Date).Days,

                        IsRealTrade = shift == 0,
                        EntryShiftBars = shift
                    };

                    CalculateContexts(row, candles, entryIndex, exitIndex);

                    rows.Add(row);
                }
            }

            return rows
                .OrderBy(x => x.Ticker)
                .ThenByDescending(x => x.ProfitPercent)
                .ThenBy(x => x.HoldDays)
                .ToList();
        }

        private void CalculateContexts(
            TradeDatasetRow row,
            List<Candle> candles,
            int entryIndex,
            int exitIndex)
        {
            var entryFeatures = _featureEngine.Calculate(candles, entryIndex);
            var exitFeatures = _featureEngine.Calculate(candles, exitIndex);

            row.DistanceTo20dHigh = entryFeatures.DistanceTo20dHigh;
            row.DistanceTo52wHigh = entryFeatures.DistanceTo52wHigh;

            // Daily MA
            row.DailyMaEntry = entryFeatures.DailyMaSignedDistancePct;
            row.DailyMaExit = exitFeatures.DailyMaSignedDistancePct;
            row.DailyMaDelta = row.DailyMaExit - row.DailyMaEntry;

            // Weekly MA
            row.WeeklyMaEntry = entryFeatures.WeeklyMaSignedDistancePct;
            row.WeeklyMaExit = exitFeatures.WeeklyMaSignedDistancePct;
            row.WeeklyMaDelta = row.WeeklyMaExit - row.WeeklyMaEntry;

            // Daily RSI
            row.DailyRsiEntry = entryFeatures.DailyRSI14;
            row.DailyRsiExit = exitFeatures.DailyRSI14;
            row.DailyRsiDelta = row.DailyRsiExit - row.DailyRsiEntry;

            // Weekly RSI
            row.WeeklyRsiEntry = entryFeatures.WeeklyRSI14;
            row.WeeklyRsiExit = exitFeatures.WeeklyRSI14;
            row.WeeklyRsiDelta = row.WeeklyRsiExit - row.WeeklyRsiEntry;

            // Daily MACD
            row.DailyMacdEntry = entryFeatures.DailyMACDLineMinusSignal;
            row.DailyMacdExit = exitFeatures.DailyMACDLineMinusSignal;
            row.DailyMacdDelta = row.DailyMacdExit - row.DailyMacdEntry;

            // Weekly MACD
            row.WeeklyMacdEntry = entryFeatures.WeeklyMACDLineMinusSignal;
            row.WeeklyMacdExit = exitFeatures.WeeklyMACDLineMinusSignal;
            row.WeeklyMacdDelta = row.WeeklyMacdExit - row.WeeklyMacdEntry;

            // H4 only for same day / next day exits
            if (row.HoldDays <= 1)
            {
                row.H4MaEntry = entryFeatures.H4MaSignedDistancePct;
                row.H4MaExit = exitFeatures.H4MaSignedDistancePct;
                row.H4MaDelta = row.H4MaExit - row.H4MaEntry;

                row.H4RsiEntry = entryFeatures.RSI14;
                row.H4RsiExit = exitFeatures.RSI14;
                row.H4RsiDelta = row.H4RsiExit - row.H4RsiEntry;

                row.H4MacdEntry = entryFeatures.MACDLineMinusSignal;
                row.H4MacdExit = exitFeatures.MACDLineMinusSignal;
                row.H4MacdDelta = row.H4MacdExit - row.H4MacdEntry;
            }
            else
            {
                row.H4MaEntry = null;
                row.H4MaExit = null;
                row.H4MaDelta = null;

                row.H4RsiEntry = null;
                row.H4RsiExit = null;
                row.H4RsiDelta = null;

                row.H4MacdEntry = null;
                row.H4MacdExit = null;
                row.H4MacdDelta = null;
            }
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

        private static int FindBarIndex(List<Candle> candles, DateTime time)
        {
            int left = 0;
            int right = candles.Count - 1;

            while (left <= right)
            {
                int mid = left + ((right - left) >> 1);

                if (candles[mid].Time <= time)
                    left = mid + 1;
                else
                    right = mid - 1;
            }

            return right;
        }
    }
}