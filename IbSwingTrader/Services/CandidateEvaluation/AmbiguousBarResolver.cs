using IBApi;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;
using IbSwingTrader.Models.Stocks;

namespace IbSwingTrader.Services.CandidateEvaluation
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
