
namespace IbSwingTrader.Application.Candidates
{
    public class TradeBuilder(
        IGetCandidatesSettingsProvider settingsProvider,
        INumberTextFormatter fmt,
        ITextLogger logger) : ITradeBuilder
    {
        private readonly IGetCandidatesSettingsProvider _settingsProvider = settingsProvider;
        private readonly INumberTextFormatter _fmt = fmt;
        private readonly ITextLogger _logger = logger;

        public TradePlan Build(
            List<Candle> candles,
            List<Candle>? entryCandles = null,
            decimal? entryDiscountOverridePct = null,
            decimal? defaultProfitPctOverride = null,
            decimal? minProfitPctOverride = null,
            decimal? maxProfitPctOverride = null)
        {
            var settings = _settingsProvider.Get().TradePlan;

            if (candles == null || candles.Count < settings.MinimumCandles)
                throw new ArgumentException("Not enough candles");

            var last = candles[^1];

            var recentLow = candles
                .Skip(Math.Max(0, candles.Count - settings.StopLookbackBars))
                .Min(x => x.Low);

            var entry = BuildEntryPrice(last.Close, entryCandles, settings, entryDiscountOverridePct);
            var stop = recentLow * settings.StopBufferMultiplier;
            var riskFloor = CalculateRiskFloor(entry, entryCandles, settings);

            if (stop >= entry)
                stop = entry * settings.FallbackStopMultiplier;

            var maxAllowedStop = entry - riskFloor;
            if (maxAllowedStop > 0m && stop > maxAllowedStop)
            {
                _logger.Info(
                    $"Trade stop widened to satisfy minimum risk floor. " +
                    $"OriginalStop={_fmt.Price(stop)}, AdjustedStop={_fmt.Price(maxAllowedStop)}, RiskFloor={_fmt.Price(riskFloor)}");
                stop = maxAllowedStop;
            }

            var minAllowedStop = entry * (1m - Math.Max(settings.MaxLossPct, 0m));
            if (minAllowedStop > 0m && stop < minAllowedStop)
            {
                _logger.Info(
                    $"Trade stop tightened to respect max loss cap. " +
                    $"OriginalStop={_fmt.Price(stop)}, AdjustedStop={_fmt.Price(minAllowedStop)}, MaxLossPct={_fmt.Percent(settings.MaxLossPct)}");
                stop = minAllowedStop;
            }

            var risk = entry - stop;
            var rawExit = entry + risk * settings.RiskRewardRatio;
            var targetProfitPct = ResolveTargetProfitPct(
                candles,
                entry,
                settings,
                defaultProfitPctOverride,
                minProfitPctOverride,
                maxProfitPctOverride);
            var cappedExit = entry * (1m + targetProfitPct);
            var exit = Math.Min(rawExit, cappedExit);

            var momentumSettings = settings.MomentumExit;
            var isMomentumExit = defaultProfitPctOverride.HasValue;
            if (isMomentumExit && momentumSettings.ExitPriceBufferPct > 0m)
            {
                var bufferedExit = exit * (1m - momentumSettings.ExitPriceBufferPct);
                if (bufferedExit > entry)
                {
                    _logger.Info(
                        $"Trade momentum exit buffered below target. " +
                        $"OriginalExit={_fmt.Price(exit)}, BufferedExit={_fmt.Price(bufferedExit)}, BufferPct={_fmt.Percent(momentumSettings.ExitPriceBufferPct)}");
                    exit = bufferedExit;
                }
            }

            _logger.Info(
                $"Trade plan built. " +
                $"Entry={_fmt.Price(entry)}, Stop={_fmt.Price(stop)}, Exit={_fmt.Price(exit)}, Risk={_fmt.Price(risk)}, RawExit={_fmt.Price(rawExit)}, " +
                $"TargetProfitPct={_fmt.Percent(targetProfitPct)}, MaxProfitPct={_fmt.Percent(maxProfitPctOverride ?? settings.MaxProfitPct)}");

            return new TradePlan
            {
                Entry = entry,
                Stop = stop,
                Exit = exit,
                ExitProfile = defaultProfitPctOverride.HasValue ? "momentum" : "standard"
            };
        }

        private decimal BuildEntryPrice(
            decimal fallbackEntry,
            List<Candle>? entryCandles,
            TradePlanSettings settings,
            decimal? entryDiscountOverridePct)
        {
            if (entryCandles == null || entryCandles.Count < settings.MinimumEntryCandles)
            {
                if (entryDiscountOverridePct.HasValue && entryDiscountOverridePct.Value > 0m)
                {
                    var discountedFallback = fallbackEntry * (1m - entryDiscountOverridePct.Value);
                    _logger.Info(
                        $"Trade entry deep-pullback fallback to discounted H4 close. " +
                        $"Fallback={_fmt.Price(fallbackEntry)}, DiscountPct={_fmt.Percent(entryDiscountOverridePct.Value)}, Entry={_fmt.Price(discountedFallback)}");
                    return discountedFallback > 0m ? discountedFallback : fallbackEntry;
                }

                _logger.Info(
                    $"Trade entry fallback to last H4 close. " +
                    $"M15 candles={(entryCandles?.Count ?? 0)} is below required {settings.MinimumEntryCandles}.");
                return fallbackEntry;
            }

            var ordered = entryCandles
                .OrderBy(x => x.Time)
                .ToList();

            var current = ordered[^1].Close;

            if (entryDiscountOverridePct.HasValue && entryDiscountOverridePct.Value > 0m)
            {
                var discountedEntry = current * (1m - entryDiscountOverridePct.Value);
                _logger.Info(
                    $"Trade entry set to discounted current price for deep-pullback candidate. " +
                    $"Current={_fmt.Price(current)}, DiscountPct={_fmt.Percent(entryDiscountOverridePct.Value)}, Entry={_fmt.Price(discountedEntry)}");
                return discountedEntry > 0m ? discountedEntry : fallbackEntry;
            }

            if (settings.UseCurrentPriceAsEntry)
            {
                var baselineDiscountPct = Math.Max(settings.BaselineEntryDiscountPct, 0m);
                var discountedCurrent = baselineDiscountPct > 0m
                    ? current * (1m - baselineDiscountPct)
                    : current;

                _logger.Info(
                    $"Trade entry set to discounted current M15 close. " +
                    $"Current={_fmt.Price(current)}, DiscountPct={_fmt.Percent(baselineDiscountPct)}, Entry={_fmt.Price(discountedCurrent)}");
                return discountedCurrent > 0m ? discountedCurrent : fallbackEntry;
            }

            var mean = ordered
                .TakeLast(settings.EntryMaLength)
                .Average(x => x.Close);

            var atr = CalculateAtr(ordered, settings.EntryAtrLength);
            if (atr <= 0m)
            {
                _logger.Info("Trade entry fallback to last H4 close. M15 ATR is not available.");
                return fallbackEntry;
            }

            var macdHistogram = BuildMacdHistogram(ordered.Select(x => x.Close).ToList());
            if (macdHistogram.Count < settings.EntryMacdSignalLookbackBars)
            {
                _logger.Info(
                    $"Trade entry fallback to last H4 close. " +
                    $"M15 MACD history={macdHistogram.Count} is below required {settings.EntryMacdSignalLookbackBars}.");
                return fallbackEntry;
            }

            var currentHist = macdHistogram[^1];
            var lookbackHist = macdHistogram[^settings.EntryMacdSignalLookbackBars];
            var histSlope = (currentHist - lookbackHist) / (settings.EntryMacdSignalLookbackBars - 1);

            var histScale = macdHistogram
                .TakeLast(Math.Max(6, settings.EntryMacdSignalLookbackBars))
                .Select(Math.Abs)
                .DefaultIfEmpty(0m)
                .Average();

            if (histScale <= 0m)
                histScale = atr * 0.01m;

            var normalizedMomentum = Clamp(histSlope / histScale, -1m, 1m);
            var distanceToMean = mean - current;
            var meanReversionMove = distanceToMean * settings.EntryMeanReversionWeight;
            var momentumMove = normalizedMomentum * atr * settings.EntryMomentumAtrMultiplier;
            var projectedPrice = current + meanReversionMove + momentumMove;

            var pullbackEntry = current - Math.Max(
                atr * settings.EntryPullbackAtrFraction,
                Math.Abs(distanceToMean) * 0.20m);

            var minimumDiscount = Math.Max(
                current * settings.MinimumEntryDiscountPct,
                atr * settings.MinimumEntryDiscountAtrFraction);

            var discountFloor = current - minimumDiscount;
            var projectedEntry = projectedPrice < current
                ? projectedPrice
                : Math.Min(pullbackEntry, discountFloor);

            var minEntry = current - atr * 1.25m;
            var maxEntry = discountFloor;

            if (maxEntry < minEntry)
            {
                _logger.Info(
                    $"Trade entry fallback to last H4 close. " +
                    $"Computed entry bounds are invalid: min={_fmt.Price(minEntry)}, max={_fmt.Price(maxEntry)}.");
                return fallbackEntry;
            }

            var entry = Clamp(projectedEntry, minEntry, maxEntry);

            _logger.Info(
                $"Trade entry from M15 forecast. " +
                $"Current={_fmt.Price(current)}, Mean={_fmt.Price(mean)}, DistanceToMean={_fmt.Price(distanceToMean)}, " +
                $"ATR={_fmt.Price(atr)}, MacdHist={_fmt.Generic(currentHist)}, MacdSlope={_fmt.Generic(histSlope)}, " +
                $"ProjectedPrice={_fmt.Price(projectedPrice)}, MinDiscount={_fmt.Price(minimumDiscount)}, LimitEntry={_fmt.Price(entry)}");

            return entry > 0m ? entry : fallbackEntry;
        }

        private decimal CalculateRiskFloor(
            decimal entry,
            List<Candle>? entryCandles,
            TradePlanSettings settings)
        {
            var percentFloor = entry * settings.MinimumRiskPct;
            var atrFloor = 0m;

            if (entryCandles != null && entryCandles.Count >= settings.EntryAtrLength + 1)
            {
                var ordered = entryCandles
                    .OrderBy(x => x.Time)
                    .ToList();

                var atr = CalculateAtr(ordered, settings.EntryAtrLength);
                atrFloor = atr * settings.MinimumRiskAtrMultiplier;
            }

            return Math.Max(percentFloor, atrFloor);
        }

        private decimal ResolveTargetProfitPct(
            List<Candle> candles,
            decimal entry,
            TradePlanSettings settings,
            decimal? defaultProfitPctOverride,
            decimal? minProfitPctOverride,
            decimal? maxProfitPctOverride)
        {
            var minProfitPct = minProfitPctOverride ?? settings.MinProfitPct;
            var maxProfitPct = maxProfitPctOverride ?? settings.MaxProfitPct;
            var defaultProfitPct = defaultProfitPctOverride ?? settings.DefaultProfitPct;

            var defaultPct = Clamp(
                defaultProfitPct,
                minProfitPct,
                maxProfitPct);

            if (candles.Count < 3)
                return defaultPct;

            var lookback = candles
                .TakeLast(Math.Min(settings.H4TargetLookbackBars, candles.Count))
                .OrderBy(x => x.Time)
                .ToList();

            if (lookback.Count < 3)
                return defaultPct;

            decimal? nearestResistance = null;

            for (var i = 1; i < lookback.Count - 1; i++)
            {
                var previous = lookback[i - 1];
                var current = lookback[i];
                var next = lookback[i + 1];

                var isSwingHigh = current.High >= previous.High && current.High > next.High;
                if (!isSwingHigh || current.High <= entry)
                    continue;

                if (!nearestResistance.HasValue || current.High < nearestResistance.Value)
                    nearestResistance = current.High;
            }

            if (!nearestResistance.HasValue)
                return defaultPct;

            var resistancePct = (nearestResistance.Value - entry) / entry;

            return Clamp(
                resistancePct,
                minProfitPct,
                maxProfitPct);
        }

        private static decimal CalculateAtr(List<Candle> candles, int length)
        {
            if (candles.Count < length + 1)
                return 0m;

            var ranges = new List<decimal>();

            for (var i = candles.Count - length; i < candles.Count; i++)
            {
                var current = candles[i];
                var previousClose = candles[i - 1].Close;

                var trueRange = Math.Max(
                    current.High - current.Low,
                    Math.Max(
                        Math.Abs(current.High - previousClose),
                        Math.Abs(current.Low - previousClose)));

                ranges.Add(trueRange);
            }

            return ranges.Count == 0
                ? 0m
                : ranges.Average();
        }

        private static List<decimal> BuildMacdHistogram(List<decimal> closes)
        {
            var result = new List<decimal>();

            if (closes.Count == 0)
                return result;

            decimal? ema12 = null;
            decimal? ema26 = null;
            decimal? signal = null;

            const decimal k12 = 2m / 13m;
            const decimal k26 = 2m / 27m;
            const decimal k9 = 2m / 10m;

            foreach (var close in closes)
            {
                ema12 = ema12 == null ? close : ema12.Value + (close - ema12.Value) * k12;
                ema26 = ema26 == null ? close : ema26.Value + (close - ema26.Value) * k26;

                var macd = ema12.Value - ema26.Value;
                signal = signal == null ? macd : signal.Value + (macd - signal.Value) * k9;

                result.Add(macd - signal.Value);
            }

            return result;
        }

        private static decimal Clamp(decimal value, decimal min, decimal max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }
    }
}
