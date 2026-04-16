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
            result.EvaluatedAtMarketTime = MarketTime.Now();

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
                result.DaysAfterEntry = (afterEntry[^1].Time.Date - entryCandle.Time.Date).Days;
                FillExcursionStats(result, result.ScanPrice, afterEntry);
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

            result.Outcome = "Open";
            return result;
        }

        private static CandidateEvaluationResult CreateBaseResult(CandidateDetails candidate)
        {
            return new CandidateEvaluationResult
            {
                Ticker = candidate.Ticker,
                ScanTimeMarket = candidate.Scan.ScanTimeMarket,
                EvaluatedAtMarketTime = MarketTime.Now(),
                PresetScanCode = candidate.Scan.PresetScanCode,
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
                ScanTimeMarket = candidate.Scan.ScanTimeMarket,
                EvaluatedAtMarketTime = MarketTime.Now(),
                PresetScanCode = candidate.Scan.PresetScanCode,
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

        private static void FillExcursionStats(
            CandidateEvaluationResult result,
            decimal scanPrice,
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
            result.MaxTime = maxUpCandle.Time;
            result.MinPct = RoundPct(CalcPct(scanPrice, maxDownCandle.Low));
            result.MinTime = maxDownCandle.Time;
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

    }
}
