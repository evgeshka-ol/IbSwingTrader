using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Application.Evaluation
{
    public class CandidateEvaluator(
        IContractResolver contractResolver,
        IHistoricalDataService historicalDataService,
        IAmbiguousBarResolver ambiguousBarResolver,
        ITextLogger logger) : ICandidateEvaluator
    {
        private static readonly TimeSpan MaxEvaluationWindow = TimeSpan.FromDays(7);
        private static readonly TimeSpan FreshDataSafetyLag = TimeSpan.FromMinutes(10);

        private readonly IContractResolver _contractResolver = contractResolver;
        private readonly IHistoricalDataService _historicalDataService = historicalDataService;
        private readonly IAmbiguousBarResolver _ambiguousBarResolver = ambiguousBarResolver;
        private readonly ITextLogger _logger = logger;

        public async Task<List<CandidateEvaluationResult>> EvaluateAsync(
            List<CandidateDetails> candidates)
        {
            var results = new List<CandidateEvaluationResult>();

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                _logger.Info(
                    $"Evaluation start: {i + 1}/{candidates.Count} {candidate.Ticker} [{candidate.Scan.PresetScanCode}] " +
                    $"scan={candidate.Scan.ScanTime:yyyy-MM-dd HH:mm:ss} source={candidate.CandidateSource}");

                try
                {
                    var result = await EvaluateOneAsync(candidate);
                    results.Add(result);
                    _logger.Info(
                        $"Evaluation completed: {i + 1}/{candidates.Count} {candidate.Ticker} [{candidate.Scan.PresetScanCode}] " +
                        $"outcome={result.Outcome ?? "Unknown"} entryTouched={result.EntryTouched} " +
                        $"entryTime={FormatTime(result.EntryTime)} exitTime={FormatTime(result.ExitTime)} stopTime={FormatTime(result.StopTime)}");
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        $"Evaluate failed for {candidate.Ticker} [{candidate.Scan.PresetScanCode}]: {ex.Message}");

                    results.Add(CreateErrorResult(candidate, ex.Message));
                }
            }

            return results;
        }

        private async Task<CandidateEvaluationResult> EvaluateOneAsync(
            CandidateDetails candidate)
        {
            var result = CreateBaseResult(candidate);
            result.EvaluatedAt = MarketTime.Now();

            _logger.Info($"Evaluation step: resolving contract for {candidate.Ticker}");

            var contract = await _contractResolver.ResolveStockAsync(candidate.Ticker);
            _logger.Info($"Evaluation step: contract resolved for {candidate.Ticker}");

            var start = candidate.Scan.ScanTime;
            var requestedEnd = candidate.Scan.ScanTime.Add(MaxEvaluationWindow);
            var availableNow = MarketTime.Now() - FreshDataSafetyLag;
            var end = requestedEnd <= availableNow ? requestedEnd : availableNow;

            result.EvaluationStartTime = start;
            result.EvaluationEndTime = end;

            if (end <= start)
            {
                result.Outcome = "InsufficientFutureData";
                _logger.Info($"Evaluation step: insufficient future data for {candidate.Ticker}");
                return result;
            }

            _logger.Info(
                $"Evaluation step: loading M5 candles for {candidate.Ticker} " +
                $"from {start:yyyy-MM-dd HH:mm:ss} to {end:yyyy-MM-dd HH:mm:ss}");

            var candles = await _historicalDataService.GetCandlesRange(
                candidate.Ticker,
                contract,
                Timeframe.M5,
                start,
                end);

            _logger.Info($"Evaluation step: candles loaded for {candidate.Ticker}. Count={candles?.Count ?? 0}");

            if (candles == null || candles.Count == 0)
            {
                result.Outcome = "NoData";
                _logger.Info($"Evaluation step: no data for {candidate.Ticker}");
                return result;
            }

            var ordered = candles
                .Where(x => x.Time >= candidate.Scan.ScanTime && x.Time <= end)
                .OrderBy(x => x.Time)
                .ToList();

            if (ordered.Count == 0)
            {
                result.Outcome = "NoDataAfterScan";
                _logger.Info($"Evaluation step: no post-scan candles for {candidate.Ticker}");
                return result;
            }

            var entryPrice = candidate.TradePlan.EntryPrice;
            var exitPrice = candidate.TradePlan.ExitPrice;
            var stopPrice = candidate.TradePlan.StopLoss;

            result.ScanPrice = ordered[0].Open;
            result.CurrentPrice = ordered[^1].Close;
            result.ScanMovePct = RoundPct(CalcPct(result.ScanPrice, result.CurrentPrice));
            result.CurrentPct = RoundPct(CalcPct(entryPrice, result.CurrentPrice));
            result.MinLowAfterScan = ordered.Min(x => x.Low);
            result.EntryDistanceToMinAfterScanPct = RoundPct(
                CalcEntryDistanceToMinPct(
                    candidate.TradePlan.EntryPrice,
                    result.MinLowAfterScan));

            var entryCandle = ordered.FirstOrDefault(x => TouchesPrice(x, entryPrice));
            if (entryCandle == null)
            {
                result.EntryTouched = false;
                LogNoEntryDiagnostics(candidate, ordered, entryPrice);
                result.Outcome = "NoEntry";
                _logger.Info($"Evaluation step: entry not touched for {candidate.Ticker}");
                return result;
            }

            result.EntryTouched = true;
            result.EntryTime = entryCandle.Time;

            FillBeforeEntryStats(result, result.ScanPrice, ordered, entryCandle.Time);

            var afterEntry = ordered
                .Where(x => x.Time >= entryCandle.Time)
                .OrderBy(x => x.Time)
                .ToList();

            if (afterEntry.Count > 0)
            {
                result.DaysAfterEntry = (afterEntry[^1].Time.Date - entryCandle.Time.Date).Days;
                FillExcursionStats(result, result.ScanPrice, entryPrice, exitPrice, afterEntry);
            }

            foreach (var candle in afterEntry)
            {
                var hitExit = TouchesPrice(candle, exitPrice);
                var hitStop = TouchesPrice(candle, stopPrice);

                if (hitExit && result.ExitTime == null)
                {
                    result.ExitTouched = true;
                    result.ExitTime = candle.Time;
                    FillAfterExitStats(result, afterEntry, exitPrice, candle.Time);
                }

                if (hitStop && result.StopTime == null)
                {
                    result.StopTouched = true;
                    result.StopTime = candle.Time;
                }

                if (hitExit && hitStop)
                {
                    var resolution = await _ambiguousBarResolver.ResolveLongAsync(
                        candidate,
                        contract,
                        candle,
                        entryPrice,
                        exitPrice,
                        stopPrice);

                    if (resolution != null)
                    {
                        if (resolution.ExitBeforeStop)
                        {
                            result.ExitTouched = true;
                            result.ExitTime = resolution.ExitTime ?? candle.Time;
                            FillAfterExitStats(result, afterEntry, exitPrice, result.ExitTime.Value);
                            result.ExitBeforeStop = true;
                            result.StopBeforeExit = false;
                            result.RealizedPct = RoundPct(CalcPct(entryPrice, exitPrice));
                            result.Outcome = "Win";
                        }
                        else
                        {
                            result.StopTouched = true;
                            result.StopTime = resolution.StopTime ?? candle.Time;
                            result.StopBeforeExit = true;
                            result.ExitBeforeStop = false;
                            result.RealizedPct = RoundPct(CalcPct(entryPrice, stopPrice));
                            result.Outcome = "Loss";
                        }

                        return result;
                    }

                    if (candle.Close > candle.Open)
                    {
                        result.ExitTouched = true;
                        result.ExitTime = candle.Time;
                        FillAfterExitStats(result, afterEntry, exitPrice, candle.Time);
                        result.ExitBeforeStop = true;
                        result.StopBeforeExit = false;
                        result.RealizedPct = RoundPct(CalcPct(entryPrice, exitPrice));
                        result.Outcome = "Win";
                    }
                    else if (candle.Close < candle.Open)
                    {
                        result.StopTouched = true;
                        result.StopTime = candle.Time;
                        result.StopBeforeExit = true;
                        result.ExitBeforeStop = false;
                        result.RealizedPct = RoundPct(CalcPct(entryPrice, stopPrice));
                        result.Outcome = "Loss";
                    }
                    else
                    {
                        var closeToExit = Math.Abs(candle.Close - exitPrice);
                        var closeToStop = Math.Abs(candle.Close - stopPrice);

                        if (closeToExit < closeToStop)
                        {
                            result.ExitTouched = true;
                            result.ExitTime = candle.Time;
                            FillAfterExitStats(result, afterEntry, exitPrice, candle.Time);
                            result.ExitBeforeStop = true;
                            result.StopBeforeExit = false;
                            result.RealizedPct = RoundPct(CalcPct(entryPrice, exitPrice));
                            result.Outcome = "Win";
                        }
                        else
                        {
                            result.StopTouched = true;
                            result.StopTime = candle.Time;
                            result.StopBeforeExit = true;
                            result.ExitBeforeStop = false;
                            result.RealizedPct = RoundPct(CalcPct(entryPrice, stopPrice));
                            result.Outcome = "Loss";
                        }
                    }

                    return result;
                }

                if (hitStop)
                {
                    result.StopTouched = true;
                    result.StopTime = candle.Time;
                    result.StopBeforeExit = true;
                    result.ExitBeforeStop = false;
                    result.RealizedPct = RoundPct(CalcPct(entryPrice, stopPrice));
                    result.Outcome = "Loss";
                    return result;
                }

                if (hitExit)
                {
                    result.ExitTouched = true;
                    result.ExitTime = candle.Time;
                    FillAfterExitStats(result, afterEntry, exitPrice, candle.Time);
                    result.ExitBeforeStop = true;
                    result.StopBeforeExit = false;
                    result.RealizedPct = RoundPct(CalcPct(entryPrice, exitPrice));
                    result.Outcome = "Win";
                    return result;
                }
            }

            ApplyUnambiguousOutcomeCorrection(result, entryPrice, exitPrice, stopPrice);

            if (result.Outcome is "Win" or "Loss")
            {
                _logger.Info($"Evaluation step: resolved outcome for {candidate.Ticker} via correction loop as {result.Outcome}");
                return result;
            }

            result.Outcome = "Open";
            _logger.Info($"Evaluation step: position still open for {candidate.Ticker}");
            return result;
        }

        private static CandidateEvaluationResult CreateBaseResult(CandidateDetails candidate)
        {
            return new CandidateEvaluationResult
            {
                Ticker = candidate.Ticker,
                ScanTime = candidate.Scan.ScanTime,
                EvaluatedAt = MarketTime.Now(),
                PresetScanCode = candidate.Scan.PresetScanCode,
                CandidateSource = candidate.CandidateSource,
                RecentDailyMaSeries = [.. candidate.RecentDailyMaSeries],
                RecentDailyRsiSeries = [.. candidate.RecentDailyRsiSeries],
                RecentDailyMacdSeries = [.. candidate.RecentDailyMacdSeries],
                RecentWeeklyMaSeries = [.. candidate.RecentWeeklyMaSeries],
                RecentWeeklyRsiSeries = [.. candidate.RecentWeeklyRsiSeries],
                RecentWeeklyMacdSeries = [.. candidate.RecentWeeklyMacdSeries],
                RecentH4MaSeries = [.. candidate.RecentH4MaSeries],
                RecentH4RsiSeries = [.. candidate.RecentH4RsiSeries],
                RecentH4MacdSeries = [.. candidate.RecentH4MacdSeries],
                IsFromWishlist = candidate.IsFromWishlist,
                StrategyVersion = 6,
                CandidateScore = candidate.Score.Score,
                EntryPrice = candidate.TradePlan.EntryPrice,
                ExitPrice = candidate.TradePlan.ExitPrice,
                StopLoss = candidate.TradePlan.StopLoss
            };
        }

        private static CandidateEvaluationResult CreateErrorResult(
            CandidateDetails candidate,
            string error)
        {
            return new CandidateEvaluationResult
            {
                Ticker = candidate.Ticker,
                ScanTime = candidate.Scan.ScanTime,
                EvaluatedAt = MarketTime.Now(),
                PresetScanCode = candidate.Scan.PresetScanCode,
                CandidateSource = candidate.CandidateSource,
                RecentDailyMaSeries = [.. candidate.RecentDailyMaSeries],
                RecentDailyRsiSeries = [.. candidate.RecentDailyRsiSeries],
                RecentDailyMacdSeries = [.. candidate.RecentDailyMacdSeries],
                RecentWeeklyMaSeries = [.. candidate.RecentWeeklyMaSeries],
                RecentWeeklyRsiSeries = [.. candidate.RecentWeeklyRsiSeries],
                RecentWeeklyMacdSeries = [.. candidate.RecentWeeklyMacdSeries],
                RecentH4MaSeries = [.. candidate.RecentH4MaSeries],
                RecentH4RsiSeries = [.. candidate.RecentH4RsiSeries],
                RecentH4MacdSeries = [.. candidate.RecentH4MacdSeries],
                IsFromWishlist = candidate.IsFromWishlist,
                StrategyVersion = 6,
                CandidateScore = candidate.Score.Score,
                EntryPrice = candidate.TradePlan.EntryPrice,
                ExitPrice = candidate.TradePlan.ExitPrice,
                StopLoss = candidate.TradePlan.StopLoss,
                Outcome = $"Error: {error}"
            };
        }

        private static bool TouchesPrice(Candle candle, decimal price)
        {
            return candle.Low <= price && candle.High >= price;
        }

        private static decimal CalcPct(decimal from, decimal to)
        {
            if (from == 0m)
                return 0m;

            return (to - from) / from * 100m;
        }

        private static decimal CalcEntryDistanceToMinPct(decimal entryPrice, decimal minLowAfterScan)
        {
            if (entryPrice <= 0m)
                return 0m;

            return (entryPrice - minLowAfterScan) / entryPrice * 100m;
        }

        private static decimal RoundPct(decimal value)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static string FormatTime(DateTime? value)
        {
            return value?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        }

        private static void ApplyUnambiguousOutcomeCorrection(
            CandidateEvaluationResult result,
            decimal entryPrice,
            decimal exitPrice,
            decimal stopPrice)
        {
            if (!result.EntryTouched)
                return;

            var exitWasReachable = result.MaxPrice.HasValue && exitPrice > 0m && result.MaxPrice.Value >= exitPrice;
            var stopWasReachable = result.MinPrice.HasValue && stopPrice > 0m && result.MinPrice.Value <= stopPrice;

            if (exitWasReachable && !stopWasReachable)
            {
                result.ExitTouched = true;
                result.StopTouched = false;
                result.ExitBeforeStop = true;
                result.StopBeforeExit = false;
                result.ExitTime ??= result.MaxTime;
                result.RealizedPct = RoundPct(CalcPct(entryPrice, exitPrice));
                result.Outcome = "Win";
                return;
            }

            if (stopWasReachable && !exitWasReachable)
            {
                result.ExitTouched = false;
                result.StopTouched = true;
                result.ExitBeforeStop = false;
                result.StopBeforeExit = true;
                result.StopTime ??= result.MinTime;
                result.RealizedPct = RoundPct(CalcPct(entryPrice, stopPrice));
                result.Outcome = "Loss";
            }
        }

        private static void FillExcursionStats(
            CandidateEvaluationResult result,
            decimal scanPrice,
            decimal entryPrice,
            decimal exitPrice,
            List<Candle> afterEntry)
        {
            if (scanPrice <= 0m || afterEntry.Count == 0)
                return;

            var maxUpCandle = afterEntry
                .OrderByDescending(x => x.High)
                .ThenBy(x => x.Time)
                .First();

            var maxDownCandle = afterEntry
                .OrderBy(x => x.Low)
                .ThenBy(x => x.Time)
                .First();

            result.MaxPct = RoundPct(CalcPct(scanPrice, maxUpCandle.High));
            result.MaxPrice = RoundPct(maxUpCandle.High);
            result.MaxTime = maxUpCandle.Time;
            result.MinPct = RoundPct(CalcPct(scanPrice, maxDownCandle.Low));
            result.MinPrice = RoundPct(maxDownCandle.Low);
            result.MinTime = maxDownCandle.Time;
            result.ExtremumOrder = GetExtremumOrder(result.MinTime, result.MaxTime);
            result.MinutesFromMinToMax = DiffMinutes(result.MinTime, result.MaxTime);
            result.MinutesFromEntryToMax = DiffMinutes(result.EntryTime, result.MaxTime);
            result.MinutesFromEntryToMin = DiffMinutes(result.EntryTime, result.MinTime);
            result.PostMaxDrawdownPct = RoundNullable(CalculatePostMaxDrawdownPct(maxUpCandle, afterEntry));
            FillExitMissStats(result, maxUpCandle.High, exitPrice);
        }

        private static void FillBeforeEntryStats(
            CandidateEvaluationResult result,
            decimal scanPrice,
            List<Candle> ordered,
            DateTime entryTime)
        {
            if (scanPrice <= 0m || ordered.Count == 0)
                return;

            var beforeEntry = ordered
                .Where(x => x.Time <= entryTime)
                .OrderBy(x => x.Time)
                .ToList();

            if (beforeEntry.Count == 0)
                return;

            var maxBeforeEntry = beforeEntry
                .OrderByDescending(x => x.High)
                .ThenBy(x => x.Time)
                .First();

            var minBeforeEntry = beforeEntry
                .OrderBy(x => x.Low)
                .ThenBy(x => x.Time)
                .First();

            result.MaxPctBeforeEntry = RoundPct(CalcPct(scanPrice, maxBeforeEntry.High));
            result.MaxPriceBeforeEntry = RoundPct(maxBeforeEntry.High);
            result.MaxTimeBeforeEntry = maxBeforeEntry.Time;
            result.MinPctBeforeEntry = RoundPct(CalcPct(scanPrice, minBeforeEntry.Low));
            result.MinPriceBeforeEntry = RoundPct(minBeforeEntry.Low);
            result.MinTimeBeforeEntry = minBeforeEntry.Time;

            var entryUndercutAbs = Math.Max(result.EntryPrice - minBeforeEntry.Low, 0m);
            result.EntryUndercutBeforeEntryAbs = RoundPct(entryUndercutAbs);
            result.EntryUndercutBeforeEntryPct = entryUndercutAbs > 0m
                ? RoundPct((entryUndercutAbs / result.EntryPrice) * 100m)
                : 0m;
        }

        private static void FillAfterExitStats(
            CandidateEvaluationResult result,
            List<Candle> afterEntry,
            decimal exitPrice,
            DateTime exitTime)
        {
            var afterExit = afterEntry
                .Where(x => x.Time >= exitTime)
                .OrderBy(x => x.Time)
                .ToList();

            if (afterExit.Count == 0 || exitPrice <= 0m)
                return;

            var maxAfterExit = afterExit
                .OrderByDescending(x => x.High)
                .ThenBy(x => x.Time)
                .First();

            result.TakeProfitOverflowPct = CalcPct(exitPrice, maxAfterExit.High);
            result.DaysAfterExitToMaxHigh = (decimal)(maxAfterExit.Time - exitTime).TotalDays;
        }

        private static void FillExitMissStats(
            CandidateEvaluationResult result,
            decimal maxHighAfterEntry,
            decimal exitPrice)
        {
            if (result.ExitTouched || exitPrice <= 0m)
            {
                result.ExitMissAbs = null;
                result.ExitMissPct = null;
                result.NearTakeProfitMiss = false;
                return;
            }

            var missAbs = Math.Max(exitPrice - maxHighAfterEntry, 0m);
            var missPct = exitPrice > 0m
                ? (missAbs / exitPrice) * 100m
                : 0m;

            result.ExitMissAbs = RoundPct(missAbs);
            result.ExitMissPct = RoundPct(missPct);
            result.NearTakeProfitMiss = missAbs > 0m && (missAbs <= 0.01m || missPct <= 0.1m);
        }

        private static decimal? CalculatePostMaxDrawdownPct(Candle maxUpCandle, List<Candle> afterEntry)
        {
            if (maxUpCandle.High <= 0m)
                return null;

            var afterMax = afterEntry
                .Where(x => x.Time >= maxUpCandle.Time)
                .OrderBy(x => x.Time)
                .ToList();

            if (afterMax.Count == 0)
                return null;

            var minLowAfterMax = afterMax.Min(x => x.Low);
            return ((maxUpCandle.High - minLowAfterMax) / maxUpCandle.High) * 100m;
        }

        private static string GetExtremumOrder(DateTime? minTime, DateTime? maxTime)
        {
            if (minTime.HasValue && maxTime.HasValue)
            {
                if (minTime.Value < maxTime.Value)
                    return "MinFirst";

                if (maxTime.Value < minTime.Value)
                    return "MaxFirst";

                return "SameBar";
            }

            if (minTime.HasValue)
                return "OnlyMin";

            if (maxTime.HasValue)
                return "OnlyMax";

            return "Unknown";
        }

        private static int? DiffMinutes(DateTime? from, DateTime? to)
        {
            if (!from.HasValue || !to.HasValue)
                return null;

            return (int)Math.Round((to.Value - from.Value).TotalMinutes, MidpointRounding.AwayFromZero);
        }

        private static decimal? RoundNullable(decimal? value)
        {
            return value.HasValue ? RoundPct(value.Value) : null;
        }

        private void LogNoEntryDiagnostics(
            CandidateDetails candidate,
            List<Candle> ordered,
            decimal entryPrice)
        {
            if (ordered.Count == 0)
            {
                _logger.Info(
                    $"NoEntry diagnostics for {candidate.Ticker}: no candles after scan. Entry={entryPrice}");
                return;
            }

            var minLow = ordered.Min(x => x.Low);
            var maxHigh = ordered.Max(x => x.High);
            var nearestIndex = FindNearestCandleIndex(ordered, entryPrice);
            var from = Math.Max(0, nearestIndex - 2);
            var to = Math.Min(ordered.Count - 1, nearestIndex + 2);

            _logger.Info(
                $"NoEntry diagnostics for {candidate.Ticker}: " +
                $"Entry={entryPrice}, ScanTime={candidate.Scan.ScanTime:yyyy-MM-dd HH:mm:ss}, " +
                $"Candles={ordered.Count}, MinLowAfterScan={minLow}, MaxHighAfterScan={maxHigh}");

            for (var i = from; i <= to; i++)
            {
                var candle = ordered[i];
                _logger.Info(
                    $"NoEntry candle {i}: " +
                    $"Time={candle.Time:yyyy-MM-dd HH:mm:ss}, " +
                    $"O={candle.Open}, H={candle.High}, L={candle.Low}, C={candle.Close}");
            }
        }

        private static int FindNearestCandleIndex(
            List<Candle> candles,
            decimal entryPrice)
        {
            var bestIndex = 0;
            var bestDistance = decimal.MaxValue;

            for (var i = 0; i < candles.Count; i++)
            {
                var candle = candles[i];
                var distance = candle.Low <= entryPrice && candle.High >= entryPrice
                    ? 0m
                    : Math.Min(Math.Abs(candle.Low - entryPrice), Math.Abs(candle.High - entryPrice));

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

    }
}
