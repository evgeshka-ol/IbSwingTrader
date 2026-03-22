using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateEvaluation
{
    public class CandidateEvaluator : ICandidateEvaluator
    {
        private static readonly TimeSpan MaxEvaluationWindow = TimeSpan.FromDays(7);
        private static readonly TimeSpan FreshDataSafetyLag = TimeSpan.FromMinutes(10);

        private readonly IContractResolver _contractResolver;
        private readonly IHistoricalDataService _historicalDataService;
        private readonly IAmbiguousBarResolver _ambiguousBarResolver;
        private readonly ITextLogger _logger;

        public CandidateEvaluator(
            IContractResolver contractResolver,
            IHistoricalDataService historicalDataService,
            IAmbiguousBarResolver ambiguousBarResolver,
            ITextLogger logger)
        {
            _contractResolver = contractResolver;
            _historicalDataService = historicalDataService;
            _ambiguousBarResolver = ambiguousBarResolver;
            _logger = logger;
        }

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
                        $"Evaluate failed for {candidate.Ticker} [{candidate.PresetScanCode}]: {ex.Message}");

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

            var start = candidate.ScanTimeMarket;
            var requestedEnd = candidate.ScanTimeMarket.Add(MaxEvaluationWindow);
            var availableNow = DateTime.UtcNow - FreshDataSafetyLag;
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
                .Where(x => x.Time >= candidate.ScanTimeMarket && x.Time <= end)
                .OrderBy(x => x.Time)
                .ToList();

            if (ordered.Count == 0)
            {
                result.Outcome = "NoDataAfterScan";
                return result;
            }

            var entryCandle = ordered.FirstOrDefault(x => TouchesPrice(x, candidate.EntryPrice));
            if (entryCandle == null)
            {
                result.EntryTouched = false;
                result.Outcome = "NoEntry";
                return result;
            }

            result.EntryTouched = true;
            result.EntryTime = entryCandle.Time;

            var afterEntry = ordered
                .Where(x => x.Time >= entryCandle.Time)
                .OrderBy(x => x.Time)
                .ToList();

            FillWindowStatsFromEntry(result, afterEntry, candidate.EntryPrice, TimeSpan.FromDays(1), 1);
            FillWindowStatsFromEntry(result, afterEntry, candidate.EntryPrice, TimeSpan.FromDays(2), 2);
            FillWindowStatsFromEntry(result, afterEntry, candidate.EntryPrice, TimeSpan.FromDays(5), 5);

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

            result.HitPlus5BeforeMinus5 = HitTargetBeforeStop(afterEntry, candidate.EntryPrice, 5m, 5m);
            result.HitPlus7BeforeMinus5 = HitTargetBeforeStop(afterEntry, candidate.EntryPrice, 7m, 5m);
            result.HitPlus10BeforeMinus5 = HitTargetBeforeStop(afterEntry, candidate.EntryPrice, 10m, 5m);

            foreach (var candle in afterEntry)
            {
                var hitExit = TouchesPrice(candle, candidate.ExitPrice);
                var hitStop = TouchesPrice(candle, candidate.StopLoss);

                if (hitExit && result.ExitTime == null)
                {
                    result.ExitTouched = true;
                    result.ExitTime = candle.Time;
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
                        candidate.EntryPrice,
                        candidate.ExitPrice,
                        candidate.StopLoss);

                    if (resolution != null)
                    {
                        if (resolution.ExitBeforeStop)
                        {
                            result.ExitTouched = true;
                            result.ExitTime = resolution.ExitTime ?? candle.Time;
                            result.ExitBeforeStop = true;
                            result.StopBeforeExit = false;
                            result.RealizedPct = CalcPct(candidate.EntryPrice, candidate.ExitPrice);
                            result.Outcome = "Win";
                        }
                        else
                        {
                            result.StopTouched = true;
                            result.StopTime = resolution.StopTime ?? candle.Time;
                            result.StopBeforeExit = true;
                            result.ExitBeforeStop = false;
                            result.RealizedPct = CalcPct(candidate.EntryPrice, candidate.StopLoss);
                            result.Outcome = "Loss";
                        }

                        return result;
                    }

                    if (candle.Close > candle.Open)
                    {
                        result.ExitTouched = true;
                        result.ExitTime = candle.Time;
                        result.ExitBeforeStop = true;
                        result.StopBeforeExit = false;
                        result.RealizedPct = CalcPct(candidate.EntryPrice, candidate.ExitPrice);
                        result.Outcome = "Win";
                    }
                    else if (candle.Close < candle.Open)
                    {
                        result.StopTouched = true;
                        result.StopTime = candle.Time;
                        result.StopBeforeExit = true;
                        result.ExitBeforeStop = false;
                        result.RealizedPct = CalcPct(candidate.EntryPrice, candidate.StopLoss);
                        result.Outcome = "Loss";
                    }
                    else
                    {
                        var closeToExit = Math.Abs(candle.Close - candidate.ExitPrice);
                        var closeToStop = Math.Abs(candle.Close - candidate.StopLoss);

                        if (closeToExit < closeToStop)
                        {
                            result.ExitTouched = true;
                            result.ExitTime = candle.Time;
                            result.ExitBeforeStop = true;
                            result.StopBeforeExit = false;
                            result.RealizedPct = CalcPct(candidate.EntryPrice, candidate.ExitPrice);
                            result.Outcome = "Win";
                        }
                        else
                        {
                            result.StopTouched = true;
                            result.StopTime = candle.Time;
                            result.StopBeforeExit = true;
                            result.ExitBeforeStop = false;
                            result.RealizedPct = CalcPct(candidate.EntryPrice, candidate.StopLoss);
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
                    result.RealizedPct = CalcPct(candidate.EntryPrice, candidate.StopLoss);
                    result.Outcome = "Loss";
                    return result;
                }

                if (hitExit)
                {
                    result.ExitTouched = true;
                    result.ExitTime = candle.Time;
                    result.ExitBeforeStop = true;
                    result.StopBeforeExit = false;
                    result.RealizedPct = CalcPct(candidate.EntryPrice, candidate.ExitPrice);
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
                ScanTimeNy = candidate.ScanTimeMarket,
                PresetScanCode = candidate.PresetScanCode,
                CandidateScore = candidate.Score,
                EntryPrice = candidate.EntryPrice,
                ExitPrice = candidate.ExitPrice,
                StopLoss = candidate.StopLoss
            };
        }

        private static CandidateEvaluationResult CreateErrorResult(
            CandidateDetails candidate,
            string error)
        {
            return new CandidateEvaluationResult
            {
                Ticker = candidate.Ticker,
                ScanTimeNy = candidate.ScanTimeMarket,
                PresetScanCode = candidate.PresetScanCode,
                CandidateScore = candidate.Score,
                EntryPrice = candidate.EntryPrice,
                ExitPrice = candidate.ExitPrice,
                StopLoss = candidate.StopLoss,
                Outcome = $"Error: {error}"
            };
        }

        private static bool TouchesPrice(Candle candle, decimal price)
        {
            return candle.Low <= price && candle.High >= price;
        }

        private static decimal CalcPct(decimal from, decimal to)
        {
            if (from == 0)
                return 0m;

            return (to - from) / from * 100m;
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