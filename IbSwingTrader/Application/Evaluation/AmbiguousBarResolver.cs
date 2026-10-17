using IBApi;

namespace IbSwingTrader.Application.Evaluation
{
    public class AmbiguousBarResolver : IAmbiguousBarResolver
    {
        private readonly IHistoricalDataService _historicalDataService;
        private readonly ITextLogger _logger;

        public AmbiguousBarResolver(
            IHistoricalDataService historicalDataService,
            ITextLogger logger)
        {
            _historicalDataService = historicalDataService;
            _logger = logger;
        }

        public async Task<List<Candle>?> ResolveEntryBarAsync(
            CandidateDetails candidate, Contract contract, Candle parentCandle, decimal entryPrice)
        {
            try
            {
                var start = parentCandle.Time;
                var subCandles = await _historicalDataService.GetM1InsideM5(candidate.Ticker, contract, start);
                var ordered = (subCandles ?? []).Where(x => x.Time >= start && x.Time < start.AddMinutes(5))
                    .GroupBy(x => x.Time).Select(x => x.Last()).OrderBy(x => x.Time).ToList();
                if (ordered.Count != 5 || ordered.Select((x, i) => x.Time == start.AddMinutes(i)).Any(x => !x) ||
                    ordered.Any(x => x.Open <= 0m || x.Close <= 0m || x.Low <= 0m ||
                        x.High < Math.Max(x.Open, x.Close) || x.Low > Math.Min(x.Open, x.Close)))
                {
                    _logger.Warning($"Entry-bar sequence unresolved: {candidate.Ticker}, M5={start:O}, incomplete M1 coverage");
                    return null;
                }
                var first = ordered.FindIndex(x => TouchesPrice(x, entryPrice));
                if (first < 0)
                {
                    _logger.Warning($"Entry-bar sequence unresolved: {candidate.Ticker}, M5={start:O}, M1 does not reproduce entry");
                    return null;
                }
                var replay = ordered.Skip(first).ToList();
                var entryMinute = replay[0];
                if (entryMinute.Open > entryPrice)
                {
                    // Its full high can predate entry. Only the path from entry to
                    // the final close is certain; do not guess an intraminute order.
                    replay[0] = new Candle
                    {
                        Timeframe = Timeframe.M1, Time = entryMinute.Time,
                        Open = entryPrice, Close = entryMinute.Close,
                        High = Math.Max(entryPrice, entryMinute.Close), Low = entryMinute.Low,
                        Volume = entryMinute.Volume
                    };
                }
                _logger.Info($"Entry-bar sequence replay: {candidate.Ticker}, M5={start:O}, " +
                    $"EntryMinute={entryMinute.Time:O}, ExcludedMinutes={first}, ConservativeEntryMinute={entryMinute.Open > entryPrice}");
                return replay;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Entry-bar sequence unavailable: {candidate.Ticker}. {ex.Message}");
                return null;
            }
        }

        public async Task<AmbiguousBarResolutionResult?> ResolveLongAsync(
            CandidateDetails candidate,
            Contract contract,
            Candle parentCandle,
            decimal entryPrice,
            decimal exitPrice,
            decimal stopLoss)
        {
            var start = parentCandle.Time;
            var end = GetCandleEndTime(parentCandle);

            var subCandles = await _historicalDataService.GetCandlesRange(
                candidate.Ticker,
                contract,
                Timeframe.M1,
                start,
                end);

            if (subCandles == null || subCandles.Count == 0)
            {
                _logger.Warning(
                    $"Ambiguous bar unresolved for {candidate.Ticker}: no M1 candles inside {parentCandle.Time:yyyy-MM-dd HH:mm:ss}");

                return null;
            }

            var ordered = subCandles
                .Where(x => x.Time >= start && x.Time < end)
                .OrderBy(x => x.Time)
                .ToList();

            var entryActivated = false;

            foreach (var candle in ordered)
            {
                if (!entryActivated)
                {
                    if (TouchesPrice(candle, entryPrice))
                        entryActivated = true;
                    else
                        continue;
                }

                var hitExit = TouchesPrice(candle, exitPrice);
                var hitStop = TouchesPrice(candle, stopLoss);

                if (hitExit && hitStop)
                {
                    if (candle.Close > candle.Open)
                    {
                        return new AmbiguousBarResolutionResult
                        {
                            ExitBeforeStop = true,
                            ExitTime = candle.Time
                        };
                    }

                    if (candle.Close < candle.Open)
                    {
                        return new AmbiguousBarResolutionResult
                        {
                            ExitBeforeStop = false,
                            StopTime = candle.Time
                        };
                    }

                    var closeToExit = Math.Abs(candle.Close - exitPrice);
                    var closeToStop = Math.Abs(candle.Close - stopLoss);

                    if (closeToExit < closeToStop)
                    {
                        return new AmbiguousBarResolutionResult
                        {
                            ExitBeforeStop = true,
                            ExitTime = candle.Time
                        };
                    }

                    return new AmbiguousBarResolutionResult
                    {
                        ExitBeforeStop = false,
                        StopTime = candle.Time
                    };
                }

                if (hitStop)
                {
                    return new AmbiguousBarResolutionResult
                    {
                        ExitBeforeStop = false,
                        StopTime = candle.Time
                    };
                }

                if (hitExit)
                {
                    return new AmbiguousBarResolutionResult
                    {
                        ExitBeforeStop = true,
                        ExitTime = candle.Time
                    };
                }
            }

            _logger.Warning(
                $"Ambiguous bar unresolved for {candidate.Ticker}: M1 candles did not confirm exit/stop inside {parentCandle.Time:yyyy-MM-dd HH:mm:ss}");

            return null;
        }

        private static bool TouchesPrice(Candle candle, decimal price)
        {
            return candle.Low <= price && candle.High >= price;
        }

        private static DateTime GetCandleEndTime(Candle candle)
        {
            return candle.Timeframe switch
            {
                Timeframe.M1 => candle.Time.AddMinutes(1),
                Timeframe.M5 => candle.Time.AddMinutes(5),
                Timeframe.M15 => candle.Time.AddMinutes(15),
                Timeframe.M30 => candle.Time.AddMinutes(30),
                Timeframe.H1 => candle.Time.AddHours(1),
                Timeframe.H4 => candle.Time.AddHours(4),
                Timeframe.D1 => candle.Time.AddDays(1),
                _ => candle.Time.AddMinutes(5)
            };
        }
    }
}
