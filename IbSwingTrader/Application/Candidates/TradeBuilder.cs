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
            decimal? scanPriceOverride = null,
            decimal? scanPriceFloorOverride = null,
            decimal? entryDiscountOverridePct = null,
            TradeEntryPatternFamily entryPatternFamily = TradeEntryPatternFamily.None,
            decimal? defaultProfitPctOverride = null,
            decimal? minProfitPctOverride = null,
            decimal? maxProfitPctOverride = null,
            decimal? maxLossPctOverride = null,
            bool useScanPriceEntry = false)
        {
            var settings = _settingsProvider.Get().TradePlan;

            if (candles == null || candles.Count < settings.MinimumCandles)
                throw new ArgumentException("Not enough candles");

            var last = candles[^1];

            var recentLow = candles
                .Skip(Math.Max(0, candles.Count - settings.StopLookbackBars))
                .Min(x => x.Low);

            var entry = useScanPriceEntry
                ? ResolveScanPriceEntry(last.Close, scanPriceOverride)
                : BuildEntryPrice(
                    last.Close,
                    entryCandles,
                    settings,
                    scanPriceOverride,
                    scanPriceFloorOverride,
                    entryDiscountOverridePct,
                    entryPatternFamily);
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

            var targetProfitPct = ResolveTargetProfitPct(
                candles,
                entry,
                settings,
                defaultProfitPctOverride,
                minProfitPctOverride,
                maxProfitPctOverride);

            var effectiveMaxLossPct = Math.Max(maxLossPctOverride ?? settings.MaxLossPct, 0m);
            var isFastTrade =
                defaultProfitPctOverride.HasValue ||
                targetProfitPct <= settings.FastTradeProfitPctThreshold;

            if (settings.CapLossToTargetProfitForFastTrades && isFastTrade)
            {
                var cappedFastTradeLossPct = Math.Min(effectiveMaxLossPct, Math.Max(targetProfitPct, 0m));
                if (cappedFastTradeLossPct < effectiveMaxLossPct)
                {
                    _logger.Info(
                        $"Trade stop tightened to not exceed target profit for fast trade. " +
                        $"OriginalMaxLossPct={_fmt.Percent(effectiveMaxLossPct)}, AdjustedMaxLossPct={_fmt.Percent(cappedFastTradeLossPct)}, " +
                        $"TargetProfitPct={_fmt.Percent(targetProfitPct)}");
                    effectiveMaxLossPct = cappedFastTradeLossPct;
                }
            }

            var minAllowedStop = entry * (1m - effectiveMaxLossPct);
            if (minAllowedStop > 0m && stop < minAllowedStop)
            {
                _logger.Info(
                    $"Trade stop tightened to respect max loss cap. " +
                    $"OriginalStop={_fmt.Price(stop)}, AdjustedStop={_fmt.Price(minAllowedStop)}, MaxLossPct={_fmt.Percent(effectiveMaxLossPct)}");
                stop = minAllowedStop;
            }

            var stopLimit = stop * (1m - Math.Max(settings.StopLimitOffsetPct, 0m));
            if (stopLimit <= 0m || stopLimit >= stop)
                stopLimit = stop * 0.999m;

            var risk = entry - stop;
            var rawExit = entry + risk * settings.RiskRewardRatio;
            var cappedExit = entry * (1m + targetProfitPct);

            // A tight, well-placed stop (small risk) is a sign of a precise entry, not a reason to
            // shrink the target. When a pattern-driven profile already set an explicit profit target
            // (defaultProfitPctOverride), use it directly instead of also capping it to a multiple of
            // risk - otherwise the tighter the stop, the more the RiskRewardRatio floor undercuts the
            // profile's own target, which defeats profiles built to capture a bigger explosive move.
            var exit = defaultProfitPctOverride.HasValue
                ? cappedExit
                : Math.Min(rawExit, cappedExit);

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
                $"Entry={_fmt.Price(entry)}, Stop={_fmt.Price(stop)}, StopLimit={_fmt.Price(stopLimit)}, Exit={_fmt.Price(exit)}, Risk={_fmt.Price(risk)}, RawExit={_fmt.Price(rawExit)}, " +
                $"TargetProfitPct={_fmt.Percent(targetProfitPct)}, MaxProfitPct={_fmt.Percent(maxProfitPctOverride ?? settings.MaxProfitPct)}");

            return new TradePlan
            {
                Entry = entry,
                Stop = stop,
                StopLimit = stopLimit,
                Exit = exit,
                ExitProfile = defaultProfitPctOverride.HasValue ? "momentum" : "standard"
            };
        }

        private decimal ResolveScanPriceEntry(decimal fallbackEntry, decimal? scanPriceOverride)
        {
            var scanPrice = scanPriceOverride.GetValueOrDefault();
            var entry = scanPrice > 0m ? scanPrice : fallbackEntry;

            _logger.Info(
                $"Trade entry forced to scan price. " +
                $"Reason=Confirmed BellUp passed entry timing, entering without discount or pattern pullback. " +
                $"Entry={_fmt.Price(entry)}");

            return entry;
        }

        private decimal BuildEntryPrice(
            decimal fallbackEntry,
            List<Candle>? entryCandles,
            TradePlanSettings settings,
            decimal? scanPriceOverride,
            decimal? scanPriceFloorOverride,
            decimal? entryDiscountOverridePct,
            TradeEntryPatternFamily entryPatternFamily)
        {
            var scanPrice = scanPriceOverride.GetValueOrDefault();
            var hasScanPrice = scanPrice > 0m;

            if (entryCandles == null || entryCandles.Count == 0)
            {
                var fallbackPrice = hasScanPrice ? scanPrice : fallbackEntry;
                if (entryDiscountOverridePct.HasValue && entryDiscountOverridePct.Value >= 0m)
                {
                    var discountedFallback = fallbackPrice * (1m - entryDiscountOverridePct.Value);
                    _logger.Info(
                        $"Trade entry fallback to scan price with profile discount. " +
                        $"Fallback={_fmt.Price(fallbackPrice)}, DiscountPct={_fmt.Percent(entryDiscountOverridePct.Value)}, Entry={_fmt.Price(discountedFallback)}");
                    return discountedFallback > 0m ? discountedFallback : fallbackPrice;
                }

                _logger.Info(
                    $"Trade entry fallback to scan price. " +
                    $"M15 candles={(entryCandles?.Count ?? 0)} is below required {settings.MinimumEntryCandles}.");
                return fallbackPrice;
            }

            var ordered = entryCandles
                .OrderBy(x => x.Time)
                .ToList();

            var current = ordered[^1].Close;
            var referencePrice = hasScanPrice ? scanPrice : fallbackEntry;

            if (TryResolvePatternEntry(
                    ordered,
                    entryPatternFamily,
                    settings.MinimumEntryCandles,
                    out var patternEntry,
                    out var patternDiagnostics))
            {
                if (entryDiscountOverridePct.HasValue && entryDiscountOverridePct.Value >= 0m)
                {
                    var maxDiscountFloor = referencePrice * (1m - entryDiscountOverridePct.Value);
                    if (patternEntry < maxDiscountFloor)
                    {
                        _logger.Info(
                            $"Trade entry from M15 {entryPatternFamily} capped to profile max discount. " +
                            $"PatternEntry={_fmt.Price(patternEntry)}, MaxDiscountPct={_fmt.Percent(entryDiscountOverridePct.Value)}, " +
                            $"CappedEntry={_fmt.Price(maxDiscountFloor)}");
                        patternEntry = maxDiscountFloor;
                    }
                }

                _logger.Info(
                    $"Trade entry predicted from M15 {entryPatternFamily}. " +
                    $"Current={_fmt.Price(current)}, Entry={_fmt.Price(patternEntry)}, " +
                    patternDiagnostics);
                return patternEntry;
            }

            if (ordered.Count < settings.MinimumEntryCandles)
            {
                if (entryDiscountOverridePct.HasValue && entryDiscountOverridePct.Value >= 0m)
                {
                    var discountedCurrent = referencePrice * (1m - entryDiscountOverridePct.Value);
                    _logger.Info(
                        $"Trade entry set to scan price with profile discount. " +
                        $"M15 candles={ordered.Count} is below forecast minimum {settings.MinimumEntryCandles}. " +
                        $"Reference={_fmt.Price(referencePrice)}, DiscountPct={_fmt.Percent(entryDiscountOverridePct.Value)}, Entry={_fmt.Price(discountedCurrent)}");
                    return discountedCurrent > 0m ? discountedCurrent : referencePrice;
                }

                if (settings.UseCurrentPriceAsEntry)
                {
                    var baselineDiscountPct = Math.Max(settings.BaselineEntryDiscountPct, 0m);
                    var discountedCurrent = baselineDiscountPct > 0m
                        ? referencePrice * (1m - baselineDiscountPct)
                        : referencePrice;

                    _logger.Info(
                        $"Trade entry set to scan price with baseline discount. " +
                        $"M15 candles={ordered.Count} is below forecast minimum {settings.MinimumEntryCandles}. " +
                        $"Reference={_fmt.Price(referencePrice)}, DiscountPct={_fmt.Percent(baselineDiscountPct)}, Entry={_fmt.Price(discountedCurrent)}");
                    return discountedCurrent > 0m ? discountedCurrent : referencePrice;
                }

                _logger.Info(
                    $"Trade entry fallback to scan price. " +
                    $"M15 candles={ordered.Count} is below required {settings.MinimumEntryCandles} and no current-price entry profile is active.");
                return referencePrice;
            }

            if (entryDiscountOverridePct.HasValue && entryDiscountOverridePct.Value >= 0m)
            {
                var discountedEntry = current * (1m - entryDiscountOverridePct.Value);
                _logger.Info(
                    $"Trade entry set to current M15 close with profile discount. " +
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

            if (scanPriceFloorOverride.HasValue &&
                scanPriceFloorOverride.Value > 0m &&
                entry < scanPriceFloorOverride.Value)
            {
                _logger.Info(
                    $"Trade entry raised toward scan-price floor from series structure. " +
                    $"Floor={_fmt.Price(scanPriceFloorOverride.Value)}, PreviousEntry={_fmt.Price(entry)}");
                entry = scanPriceFloorOverride.Value;
            }

            _logger.Info(
                $"Trade entry from M15 forecast. " +
                $"Current={_fmt.Price(current)}, Mean={_fmt.Price(mean)}, DistanceToMean={_fmt.Price(distanceToMean)}, " +
                $"ATR={_fmt.Price(atr)}, MacdHist={_fmt.Generic(currentHist)}, MacdSlope={_fmt.Generic(histSlope)}, " +
                $"ProjectedPrice={_fmt.Price(projectedPrice)}, MinDiscount={_fmt.Price(minimumDiscount)}, LimitEntry={_fmt.Price(entry)}");

            return entry > 0m ? entry : fallbackEntry;
        }

        private static bool TryResolvePatternEntry(
            List<Candle> candles,
            TradeEntryPatternFamily family,
            int minimumCandles,
            out decimal entry,
            out string diagnostics)
        {
            entry = 0m;
            diagnostics = string.Empty;
            if (family == TradeEntryPatternFamily.None ||
                candles.Count < Math.Max(20, minimumCandles))
                return false;

            var closes = candles.Select(x => x.Close).ToList();
            var bollinger = BuildBollingerSeries(closes, 20, 2m);
            var macd = BuildMacdHistogram(closes);
            var rsi = BuildRsiSeries(closes, 14);
            if (bollinger.Count < 6 || macd.Count < 6 || rsi.Count < 6)
                return false;

            var current = candles[^1].Close;
            var latest = bollinger[^1];
            var prior = bollinger[^3];
            var latestWidth = latest.Upper - latest.Lower;
            var priorWidth = prior.Upper - prior.Lower;
            var recentLow = candles.TakeLast(3).Min(x => x.Low);
            var histogramTurnsUp =
                macd[^1] > macd[^2] &&
                macd[^2] >= macd[^3];
            var rsiRecovers = rsi[^1] > rsi.TakeLast(5).Min();

            if (family == TradeEntryPatternFamily.BellUp)
            {
                var bandsOpenUp =
                    latest.Upper > bollinger[^2].Upper &&
                    latest.Mid >= bollinger[^2].Mid &&
                    latestWidth > priorWidth;
                var momentumConfirms =
                    histogramTurnsUp ||
                    (macd[^1] > 0m && rsi[^1] >= 50m);
                if (!bandsOpenUp || !momentumConfirms || current < latest.Mid)
                    return false;

                entry = Math.Min(current, Math.Max(latest.Mid, recentLow));
                diagnostics =
                    $"Mid={latest.Mid:0.####}, Width={latestWidth:0.####}, " +
                    $"PriorWidth={priorWidth:0.####}, Rsi={rsi[^1]:0.##}, " +
                    $"MacdHistogram={macd[^1]:0.####}";
                return entry > 0m;
            }

            var lowerHooked =
                bollinger[^4].Lower > bollinger[^3].Lower &&
                bollinger[^2].Lower >= bollinger[^3].Lower &&
                latest.Lower >= bollinger[^2].Lower;
            var channelCompresses = latestWidth <= priorWidth;
            if (!lowerHooked || !channelCompresses || !histogramTurnsUp || !rsiRecovers)
                return false;

            entry = Math.Min(current, Math.Max(latest.Lower, recentLow));
            diagnostics =
                $"Lower={latest.Lower:0.####}, Width={latestWidth:0.####}, " +
                $"PriorWidth={priorWidth:0.####}, Rsi={rsi[^1]:0.##}, " +
                $"MacdHistogram={macd[^1]:0.####}";
            return entry > 0m;
        }

        private static List<BollingerPoint> BuildBollingerSeries(
            List<decimal> closes,
            int length,
            decimal deviationMultiplier)
        {
            var result = new List<BollingerPoint>();
            for (var i = length - 1; i < closes.Count; i++)
            {
                var window = closes.Skip(i - length + 1).Take(length).ToList();
                var mean = window.Average();
                var variance = window
                    .Select(x => (x - mean) * (x - mean))
                    .Average();
                var deviation = (decimal)Math.Sqrt((double)variance);
                result.Add(new BollingerPoint(
                    mean + deviation * deviationMultiplier,
                    mean,
                    mean - deviation * deviationMultiplier));
            }

            return result;
        }

        private static List<decimal> BuildRsiSeries(List<decimal> closes, int length)
        {
            var result = new List<decimal>();
            if (closes.Count <= length)
                return result;

            decimal averageGain = 0m;
            decimal averageLoss = 0m;
            for (var i = 1; i <= length; i++)
            {
                var change = closes[i] - closes[i - 1];
                averageGain += Math.Max(change, 0m);
                averageLoss += Math.Max(-change, 0m);
            }

            averageGain /= length;
            averageLoss /= length;
            result.Add(CalculateRsi(averageGain, averageLoss));

            for (var i = length + 1; i < closes.Count; i++)
            {
                var change = closes[i] - closes[i - 1];
                averageGain = (averageGain * (length - 1) + Math.Max(change, 0m)) / length;
                averageLoss = (averageLoss * (length - 1) + Math.Max(-change, 0m)) / length;
                result.Add(CalculateRsi(averageGain, averageLoss));
            }

            return result;
        }

        private static decimal CalculateRsi(decimal averageGain, decimal averageLoss)
        {
            if (averageLoss <= 0m)
                return 100m;

            var relativeStrength = averageGain / averageLoss;
            return 100m - 100m / (1m + relativeStrength);
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
            var clampedResistancePct = Clamp(
                resistancePct,
                minProfitPct,
                maxProfitPct);

            if (clampedResistancePct < defaultPct)
            {
                _logger.Info(
                    $"Trade target kept at profile default instead of near H4 resistance. " +
                    $"ResistancePct={_fmt.Percent(clampedResistancePct)}, DefaultProfitPct={_fmt.Percent(defaultPct)}");
            }

            return Math.Max(defaultPct, clampedResistancePct);
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

        private sealed record BollingerPoint(
            decimal Upper,
            decimal Mid,
            decimal Lower);
    }
}
