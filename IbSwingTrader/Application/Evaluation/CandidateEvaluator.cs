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

            foreach (var candidate in candidates)
            {
                try
                {
                    var result = await EvaluateOneAsync(candidate);
                    results.Add(result);
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

            var contract = await _contractResolver.ResolveStockAsync(candidate.Ticker);

            var start = candidate.Scan.ScanTimeMarket;
            var requestedEnd = candidate.Scan.ScanTimeMarket.Add(MaxEvaluationWindow);
            var availableNow = MarketTime.Now() - FreshDataSafetyLag;
            var end = requestedEnd <= availableNow ? requestedEnd : availableNow;

            result.EvaluationStartTime = start;
            result.EvaluationEndTime = end;

            if (end <= start)
            {
                result.Outcome = "InsufficientFutureData";
                return result;
            }

            var candles = await _historicalDataService.GetCandlesRange(
                candidate.Ticker,
                contract,
                Timeframe.M5,
                start,
                end);

            if (candles == null || candles.Count == 0)
            {
                result.Outcome = "NoData";
                return result;
            }

            var ordered = candles
                .Where(x => x.Time >= candidate.Scan.ScanTimeMarket && x.Time <= end)
                .OrderBy(x => x.Time)
                .ToList();

            if (ordered.Count == 0)
            {
                result.Outcome = "NoDataAfterScan";
                return result;
            }

            result.MinLowAfterScan = ordered.Min(x => x.Low);
            result.EntryDistanceToMinAfterScanPct = CalcEntryDistanceToMinPct(
                candidate.TradePlan.EntryPrice,
                result.MinLowAfterScan);

            var entryPrice = candidate.TradePlan.EntryPrice;
            var exitPrice = candidate.TradePlan.ExitPrice;
            var stopPrice = candidate.TradePlan.StopLoss;

            var entryCandle = ordered.FirstOrDefault(x => TouchesPrice(x, entryPrice));
            if (entryCandle == null)
            {
                result.EntryTouched = false;
                LogNoEntryDiagnostics(candidate, ordered, entryPrice);
                result.Outcome = "NoEntry";
                return result;
            }

            result.EntryTouched = true;
            result.EntryTime = entryCandle.Time;

            var afterEntry = ordered
                .Where(x => x.Time >= entryCandle.Time)
                .OrderBy(x => x.Time)
                .ToList();

            if (afterEntry.Count > 0)
            {
                result.MaxHighAfterEntry = afterEntry.Max(x => x.High);
                result.ExitDistanceToMaxAfterEntryPct = CalcExitDistanceToMaxPct(
                    exitPrice,
                    result.MaxHighAfterEntry);
            }

            FillWindowStatsFromEntry(result, afterEntry, entryPrice, TimeSpan.FromDays(1), 1);
            FillWindowStatsFromEntry(result, afterEntry, entryPrice, TimeSpan.FromDays(2), 2);
            FillWindowStatsFromEntry(result, afterEntry, entryPrice, TimeSpan.FromDays(5), 5);

            result.Target3Pct1DHit = result.MaxMovePct1D >= 3m;
            result.Target5Pct1DHit = result.MaxMovePct1D >= 5m;
            result.Target7Pct1DHit = result.MaxMovePct1D >= 7m;
            result.Target10Pct1DHit = result.MaxMovePct1D >= 10m;
            result.Target15Pct1DHit = result.MaxMovePct1D >= 15m;

            result.Target3Pct2DHit = result.MaxMovePct2D >= 3m;
            result.Target5Pct2DHit = result.MaxMovePct2D >= 5m;
            result.Target7Pct2DHit = result.MaxMovePct2D >= 7m;
            result.Target10Pct2DHit = result.MaxMovePct2D >= 10m;
            result.Target15Pct2DHit = result.MaxMovePct2D >= 15m;

            result.Target3Pct5DHit = result.MaxMovePct5D >= 3m;
            result.Target5Pct5DHit = result.MaxMovePct5D >= 5m;
            result.Target7Pct5DHit = result.MaxMovePct5D >= 7m;
            result.Target10Pct5DHit = result.MaxMovePct5D >= 10m;
            result.Target15Pct5DHit = result.MaxMovePct5D >= 15m;

            result.HitPlus5BeforeMinus5 = HitTargetBeforeStop(afterEntry, entryPrice, 5m, 5m);
            result.HitPlus7BeforeMinus5 = HitTargetBeforeStop(afterEntry, entryPrice, 7m, 5m);
            result.HitPlus10BeforeMinus5 = HitTargetBeforeStop(afterEntry, entryPrice, 10m, 5m);

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
                            result.RealizedPct = CalcPct(entryPrice, exitPrice);
                            result.Outcome = "Win";
                        }
                        else
                        {
                            result.StopTouched = true;
                            result.StopTime = resolution.StopTime ?? candle.Time;
                            result.StopBeforeExit = true;
                            result.ExitBeforeStop = false;
                            result.RealizedPct = CalcPct(entryPrice, stopPrice);
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
                        result.RealizedPct = CalcPct(entryPrice, exitPrice);
                        result.Outcome = "Win";
                    }
                    else if (candle.Close < candle.Open)
                    {
                        result.StopTouched = true;
                        result.StopTime = candle.Time;
                        result.StopBeforeExit = true;
                        result.ExitBeforeStop = false;
                        result.RealizedPct = CalcPct(entryPrice, stopPrice);
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
                            result.RealizedPct = CalcPct(entryPrice, exitPrice);
                            result.Outcome = "Win";
                        }
                        else
                        {
                            result.StopTouched = true;
                            result.StopTime = candle.Time;
                            result.StopBeforeExit = true;
                            result.ExitBeforeStop = false;
                            result.RealizedPct = CalcPct(entryPrice, stopPrice);
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
                    result.RealizedPct = CalcPct(entryPrice, stopPrice);
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
                    result.RealizedPct = CalcPct(entryPrice, exitPrice);
                    result.Outcome = "Win";
                    return result;
                }
            }

            result.Outcome = "Open";
            return result;
        }

        private static CandidateEvaluationResult CreateBaseResult(CandidateDetails candidate)
        {
            return new CandidateEvaluationResult
            {
                Ticker = candidate.Ticker,
                ScanTimeNy = candidate.Scan.ScanTimeMarket,
                PresetScanCode = candidate.Scan.PresetScanCode,
                StrategyVersion = 2,
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
                ScanTimeNy = candidate.Scan.ScanTimeMarket,
                PresetScanCode = candidate.Scan.PresetScanCode,
                StrategyVersion = 2,
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

        private static decimal CalcExitDistanceToMaxPct(decimal exitPrice, decimal maxHighAfterEntry)
        {
            if (exitPrice <= 0m)
                return 0m;

            return (maxHighAfterEntry - exitPrice) / exitPrice * 100m;
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

            result.MaxHighAfterExit = maxAfterExit.High;
            result.TakeProfitOverflowPct = CalcPct(exitPrice, result.MaxHighAfterExit);
            result.DaysAfterExitToMaxHigh = (decimal)(maxAfterExit.Time - exitTime).TotalDays;
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
                $"Entry={entryPrice}, ScanTime={candidate.Scan.ScanTimeMarket:yyyy-MM-dd HH:mm:ss}, " +
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

        private static void FillWindowStatsFromEntry(
            CandidateEvaluationResult result,
            List<Candle> candlesAfterEntry,
            decimal entryPrice,
            TimeSpan window,
            int days)
        {
            var entryTime = result.EntryTime;
            if (entryTime == null)
                return;

            var end = entryTime.Value.Add(window);

            var range = candlesAfterEntry
                .Where(x => x.Time <= end)
                .ToList();

            if (range.Count == 0)
                return;

            var maxHigh = range.Max(x => x.High);
            var minLow = range.Min(x => x.Low);

            var maxMovePct = CalcPct(entryPrice, maxHigh);
            var maxDrawdownPct = CalcPct(entryPrice, minLow);

            switch (days)
            {
                case 1:
                    result.MaxHighAfterEntry1D = maxHigh;
                    result.MinLowAfterEntry1D = minLow;
                    result.MaxMovePct1D = maxMovePct;
                    result.MaxDrawdownPct1D = maxDrawdownPct;
                    break;

                case 2:
                    result.MaxHighAfterEntry2D = maxHigh;
                    result.MinLowAfterEntry2D = minLow;
                    result.MaxMovePct2D = maxMovePct;
                    result.MaxDrawdownPct2D = maxDrawdownPct;
                    break;

                case 5:
                    result.MaxHighAfterEntry5D = maxHigh;
                    result.MinLowAfterEntry5D = minLow;
                    result.MaxMovePct5D = maxMovePct;
                    result.MaxDrawdownPct5D = maxDrawdownPct;
                    break;
            }
        }

        private static bool? HitTargetBeforeStop(
            List<Candle> candlesAfterEntry,
            decimal entryPrice,
            decimal targetPct,
            decimal stopPct)
        {
            var targetPrice = entryPrice * (1m + targetPct / 100m);
            var stopPrice = entryPrice * (1m - stopPct / 100m);

            foreach (var candle in candlesAfterEntry)
            {
                var hitTarget = TouchesPrice(candle, targetPrice);
                var hitStop = TouchesPrice(candle, stopPrice);

                if (hitTarget && hitStop)
                {
                    if (candle.Close > candle.Open)
                        return true;

                    if (candle.Close < candle.Open)
                        return false;

                    var closeToTarget = Math.Abs(candle.Close - targetPrice);
                    var closeToStop = Math.Abs(candle.Close - stopPrice);
                    return closeToTarget < closeToStop;
                }

                if (hitTarget)
                    return true;

                if (hitStop)
                    return false;
            }

            return null;
        }
    }
}
