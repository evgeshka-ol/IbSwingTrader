
namespace IbSwingTrader.Application.Dataset
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
            var settings = _settingsProvider.Get();

            if (candles == null || candles.Count == 0)
                return rows;

            for (int t = 0; t < trades.Count; t++)
            {
                var trade = trades[t];

                var entryIndexReal = FindBarIndex(candles, trade.EntryTimeMarket);
                var exitIndexReal = FindBarIndex(candles, trade.ExitTimeMarket);

                if (entryIndexReal < 0)
                {
                    _logger.Info(
                        $"Trade {t}: Entry time {trade.EntryTimeMarket} is before first candle {candles[0].Time}");
                    continue;
                }

                if (exitIndexReal < 0)
                {
                    _logger.Info(
                        $"Trade {t}: Exit time {trade.ExitTimeMarket} is before first candle {candles[0].Time}");
                    continue;
                }

                if (exitIndexReal < entryIndexReal)
                {
                    _logger.Info(
                        $"Trade {t}: Exit index {exitIndexReal} is before entry index {entryIndexReal}");
                    continue;
                }

                var candlePriceReal = candles[entryIndexReal].Close;
                var splitFactor = DetectSplitFactor(trade.EntryPrice, candlePriceReal);
                var entryVariants = BuildEntryVariants(
                    trade,
                    candles,
                    entryIndexReal,
                    exitIndexReal,
                    settings.IncludeSyntheticTrades);

                foreach (var variant in entryVariants)
                {
                    int entryIndex = variant.EntryIndex;
                    int exitIndex = exitIndexReal;

                    if (exitIndex < entryIndex)
                    {
                        _logger.Info(
                            $"Trade {t}: Shift {variant.ShiftBars} leads to exit index {exitIndex} before entry index {entryIndex}");
                        continue;
                    }

                    var entryTime = variant.IsRealTrade
                        ? trade.EntryTimeMarket
                        : candles[entryIndex].Time;

                    var exitTime = trade.ExitTimeMarket;

                    if (entryTime >= exitTime)
                    {
                        _logger.Info(
                            $"Trade {t}: Shift {variant.ShiftBars} leads to entry time {entryTime} after or equal to exit time {exitTime}");
                        continue;
                    }

                    var holdHours = (decimal)(exitTime - entryTime).TotalHours;
                    if (holdHours < 4m)
                    {
                        _logger.Info(
                            $"Trade {t}: Shift {variant.ShiftBars} leads to hold time {_fmt.Hours(holdHours)} hours, less than 4 hours");
                        continue;
                    }

                    decimal entryPrice = variant.IsRealTrade
                        ? trade.EntryPrice / splitFactor
                        : candles[entryIndex].Close;

                    decimal exitPrice = trade.ExitPrice / splitFactor;
                    decimal side = trade.IsShort ? -1m : 1m;

                    var row = new TradeDatasetRow
                    {
                        Ticker = trade.Ticker,
                        IsShort = trade.IsShort,

                        EntryTimeMarket = entryTime,
                        EntryPrice = entryPrice,

                        ExitTimeMarket = exitTime,
                        ExitPrice = exitPrice,

                        ProfitPercent = side * (exitPrice - entryPrice) / entryPrice * 100m,
                        HoldDays = (exitTime.Date - entryTime.Date).Days,

                        IsRealTrade = variant.IsRealTrade,
                        EntryShiftBars = variant.ShiftBars,
                        EntryShiftFraction = variant.ShiftFraction
                    };

                    FillSeries(row, candles, entryIndex, exitIndex);

                    rows.Add(row);
                }
            }

            return rows
                .OrderBy(x => x.Ticker)
                .ThenByDescending(x => x.ProfitPercent)
                .ThenBy(x => x.HoldDays)
                .ToList();
        }

        private List<EntryVariant> BuildEntryVariants(
            TradeRecord trade,
            List<Candle> candles,
            int entryIndexReal,
            int exitIndexReal,
            bool includeSyntheticTrades)
        {
            var variants = new List<EntryVariant>
            {
                new(entryIndexReal, 0, 0m, true)
            };

            if (!includeSyntheticTrades || exitIndexReal <= entryIndexReal)
                return variants;

            var fractions = _settingsProvider.Get()
                .SyntheticEntryFractions
                .Where(x => x > 0m && x < 1m)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            if (fractions.Count == 0)
                return variants;

            var usedIndexes = new HashSet<int> { entryIndexReal };
            var holdTicks = trade.ExitTimeMarket.Ticks - trade.EntryTimeMarket.Ticks;

            foreach (var fraction in fractions)
            {
                var shiftTicks = (long)Math.Round(holdTicks * fraction, MidpointRounding.AwayFromZero);
                var targetTime = trade.EntryTimeMarket.AddTicks(shiftTicks);
                var entryIndex = FindFirstBarIndexAtOrAfter(candles, targetTime);

                if (entryIndex <= entryIndexReal || entryIndex >= candles.Count || entryIndex > exitIndexReal)
                    continue;

                if (!usedIndexes.Add(entryIndex))
                    continue;

                variants.Add(new EntryVariant(
                    entryIndex,
                    entryIndex - entryIndexReal,
                    fraction,
                    false));
            }

            return variants;
        }

        private void FillSeries(
            TradeDatasetRow row,
            List<Candle> candles,
            int entryIndex,
            int exitIndex)
        {
            var entryFeatures = _featureEngine.Calculate(candles, entryIndex + 1);

            row.DistanceTo20dHigh = entryFeatures.DistanceTo20dHigh;
            row.DistanceTo52wHigh = entryFeatures.DistanceTo52wHigh;

            FillDailySeries(row, candles, entryIndex, exitIndex);
            FillWeeklySeries(row, candles, entryIndex, exitIndex);

            if (row.HoldDays <= 1)
                FillH4Series(row, candles, entryIndex, exitIndex);
        }

        private void FillDailySeries(
            TradeDatasetRow row,
            List<Candle> candles,
            int entryIndex,
            int exitIndex)
        {
            DateTime? lastDay = null;

            for (int i = entryIndex; i <= exitIndex; i++)
            {
                var day = candles[i].Time.Date;

                if (lastDay.HasValue && lastDay.Value == day)
                    continue;

                lastDay = day;

                var lastBarIndexOfDay = FindLastBarIndexOfDay(candles, i, exitIndex, day);
                var features = _featureEngine.Calculate(candles, lastBarIndexOfDay + 1);

                row.DailyMaDistances.Add(features.DailyMaSignedDistancePct);
                row.DailyRsiValues.Add(features.DailyRSI14);
                row.DailyMacdValues.Add(features.DailyMACDLineMinusSignal);
            }
        }

        private void FillWeeklySeries(
            TradeDatasetRow row,
            List<Candle> candles,
            int entryIndex,
            int exitIndex)
        {
            DateTime? lastWeekStart = null;

            for (int i = entryIndex; i <= exitIndex; i++)
            {
                var weekStart = GetWeekStart(candles[i].Time);

                if (lastWeekStart.HasValue && lastWeekStart.Value == weekStart)
                    continue;

                lastWeekStart = weekStart;

                var lastBarIndexOfWeek = FindLastBarIndexOfWeek(candles, i, exitIndex, weekStart);
                var features = _featureEngine.Calculate(candles, lastBarIndexOfWeek + 1);

                if (features.WeeklyMaSignedDistancePct.HasValue)
                    row.WeeklyMaDistances.Add(features.WeeklyMaSignedDistancePct.Value);

                if (features.WeeklyRSI14.HasValue)
                    row.WeeklyRsiValues.Add(features.WeeklyRSI14.Value);

                if (features.WeeklyMACDLineMinusSignal.HasValue)
                    row.WeeklyMacdValues.Add(features.WeeklyMACDLineMinusSignal.Value);
            }
        }

        private void FillH4Series(
            TradeDatasetRow row,
            List<Candle> candles,
            int entryIndex,
            int exitIndex)
        {
            row.H4MaDistances = [];
            row.H4RsiValues = [];
            row.H4MacdValues = [];

            for (int i = entryIndex; i <= exitIndex; i++)
            {
                var features = _featureEngine.Calculate(candles, i + 1);

                row.H4MaDistances.Add(features.H4MaSignedDistancePct);
                row.H4RsiValues.Add(features.RSI14);
                row.H4MacdValues.Add(features.MACDLineMinusSignal);
            }
        }

        private static int FindLastBarIndexOfDay(
            List<Candle> candles,
            int startIndex,
            int maxIndex,
            DateTime day)
        {
            int i = startIndex;

            while (i + 1 <= maxIndex && candles[i + 1].Time.Date == day)
                i++;

            return i;
        }

        private static int FindLastBarIndexOfWeek(
            List<Candle> candles,
            int startIndex,
            int maxIndex,
            DateTime weekStart)
        {
            int i = startIndex;

            while (i + 1 <= maxIndex && GetWeekStart(candles[i + 1].Time) == weekStart)
                i++;

            return i;
        }

        private static DateTime GetWeekStart(DateTime time)
        {
            var date = time.Date;
            int diff = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
            return date.AddDays(-diff);
        }

        private static decimal DetectSplitFactor(decimal tradePrice, decimal candlePrice)
        {
            if (candlePrice <= 0m)
                return 1m;

            var ratio = tradePrice / candlePrice;
            var rounded = Math.Round(ratio);

            if (rounded >= 2m &&
                rounded <= 20m &&
                Math.Abs(ratio - rounded) < 0.2m)
            {
                return rounded;
            }

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

        private static int FindFirstBarIndexAtOrAfter(List<Candle> candles, DateTime time)
        {
            int left = 0;
            int right = candles.Count - 1;
            int answer = candles.Count;

            while (left <= right)
            {
                int mid = left + ((right - left) >> 1);

                if (candles[mid].Time >= time)
                {
                    answer = mid;
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }

            return answer;
        }

        private sealed record EntryVariant(
            int EntryIndex,
            int ShiftBars,
            decimal ShiftFraction,
            bool IsRealTrade);
    }
}
