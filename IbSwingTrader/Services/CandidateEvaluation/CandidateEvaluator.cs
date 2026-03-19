using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.CandidateEvaluation
{
    public class CandidateEvaluator : ICandidateEvaluator
    {
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

            var start = candidate.ScanTime;
            var end = candidate.ScanTime.AddDays(7);

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
                .Where(x => x.Time >= candidate.ScanTime)
                .OrderBy(x => x.Time)
                .ToList();

            if (ordered.Count == 0)
            {
                result.Outcome = "NoDataAfterScan";
                return result;
            }

            FillWindowStats(result, ordered, candidate.EntryPrice, TimeSpan.FromDays(1), 1);
            FillWindowStats(result, ordered, candidate.EntryPrice, TimeSpan.FromDays(2), 2);
            FillWindowStats(result, ordered, candidate.EntryPrice, TimeSpan.FromDays(5), 5);

            result.Target10Pct1DHit = result.MaxMovePct1D >= 10m;

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
                ScanTime = candidate.ScanTime,
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
                ScanTime = candidate.ScanTime,
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

        private static void FillWindowStats(
            CandidateEvaluationResult result,
            List<Candle> candles,
            decimal entryPrice,
            TimeSpan window,
            int days)
        {
            var end = result.ScanTime.Add(window);

            var range = candles
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
                    result.MaxHighAfterScan1D = maxHigh;
                    result.MaxMovePct1D = maxMovePct;
                    result.MaxDrawdownPct1D = maxDrawdownPct;
                    break;

                case 2:
                    result.MaxHighAfterScan2D = maxHigh;
                    result.MaxMovePct2D = maxMovePct;
                    result.MaxDrawdownPct2D = maxDrawdownPct;
                    break;

                case 5:
                    result.MaxHighAfterScan5D = maxHigh;
                    result.MaxMovePct5D = maxMovePct;
                    result.MaxDrawdownPct5D = maxDrawdownPct;
                    break;
            }
        }
    }
}
