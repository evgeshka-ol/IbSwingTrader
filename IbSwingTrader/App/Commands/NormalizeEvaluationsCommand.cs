namespace IbSwingTrader.App.Commands
{
    public class NormalizeEvaluationsCommand(
        ITwsConnection twsConnection,
        ITwsSettingsProvider twsSettingsProvider,
        IContractResolver contractResolver,
        IHistoricalDataService historicalDataService,
        ICandidateEvaluationCsvService candidateEvaluationCsvService,
        IAgentPathService pathService,
        ITextLogger logger) : ICommand
    {
        private readonly ITwsConnection _twsConnection = twsConnection;
        private readonly ITwsSettingsProvider _twsSettingsProvider = twsSettingsProvider;
        private readonly IContractResolver _contractResolver = contractResolver;
        private readonly IHistoricalDataService _historicalDataService = historicalDataService;
        private readonly ICandidateEvaluationCsvService _candidateEvaluationCsvService = candidateEvaluationCsvService;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITextLogger _logger = logger;

        public async Task RunAsync()
        {
            var evaluationsPath = _pathService.GetEvaluationsFile();
            var records = await _candidateEvaluationCsvService.ReadAsync(evaluationsPath);

            EnsureConnected(_twsSettingsProvider.Get().ConnectTimeoutSeconds);

            foreach (var record in records)
                await BackfillRecordAsync(record);

            await _candidateEvaluationCsvService.WriteAsync(evaluationsPath, records);

            _logger.Info($"Evaluations normalized: {evaluationsPath}. Records={records.Count}");
        }

        private void EnsureConnected(int timeoutSeconds)
        {
            if (_twsConnection.IsConnected)
                return;

            _logger.Info("Connecting to TWS...");

            _twsConnection.Connect();

            var connected = _twsConnection.Ready.Task
                .Wait(TimeSpan.FromSeconds(timeoutSeconds));

            if (!connected || !_twsConnection.IsConnected)
                throw new InvalidOperationException("Failed to connect to TWS.");

            _logger.Info("TWS connected.");
        }

        private async Task BackfillRecordAsync(CandidateEvaluationResult record)
        {
            record.CurrentPct = RoundNullable(CalcPct(record.EntryPrice, record.CurrentPrice));

            var evaluationEnd = record.EvaluationEndTime != null
                ? record.EvaluationEndTime.Value
                : (record.EvaluatedAtMarketTime != default
                    ? record.EvaluatedAtMarketTime
                    : record.ScanTimeMarket);

            if (evaluationEnd <= record.ScanTimeMarket)
                return;

            var contract = await _contractResolver.ResolveStockAsync(record.Ticker);
            var candles = await _historicalDataService.GetCandlesRange(
                record.Ticker,
                contract,
                Timeframe.M5,
                record.ScanTimeMarket,
                evaluationEnd);

            if (candles == null || candles.Count == 0)
                return;

            var ordered = candles
                .Where(x => x.Time >= record.ScanTimeMarket && x.Time <= evaluationEnd)
                .OrderBy(x => x.Time)
                .ToList();

            if (ordered.Count == 0)
                return;

            record.ScanPrice = ordered[0].Open;
            record.CurrentPrice = ordered[^1].Close;
            record.ScanMovePct = RoundNullable(CalcPct(record.ScanPrice, record.CurrentPrice));
            record.CurrentPct = RoundNullable(CalcPct(record.EntryPrice, record.CurrentPrice));
            record.MinLowAfterScan = ordered.Min(x => x.Low);
            record.EntryDistanceToMinAfterScanPct = Round(
                CalcEntryDistanceToMinPct(record.EntryPrice, record.MinLowAfterScan));

            if (!record.EntryTouched || !record.EntryTime.HasValue)
                return;

            FillBeforeEntryStats(record, ordered, record.EntryTime.Value);

            var afterEntry = ordered
                .Where(x => x.Time >= record.EntryTime.Value)
                .OrderBy(x => x.Time)
                .ToList();

            if (afterEntry.Count == 0)
                return;

            record.DaysAfterEntry = (afterEntry[^1].Time.Date - record.EntryTime.Value.Date).Days;

            var maxUp = afterEntry
                .OrderByDescending(x => x.High)
                .ThenBy(x => x.Time)
                .First();

            var maxDown = afterEntry
                .OrderBy(x => x.Low)
                .ThenBy(x => x.Time)
                .First();

            record.MaxPct = RoundNullable(CalcPct(record.ScanPrice, maxUp.High));
            record.MaxPrice = RoundNullable(maxUp.High);
            record.MaxTime = maxUp.Time;
            record.MinPct = RoundNullable(CalcPct(record.ScanPrice, maxDown.Low));
            record.MinPrice = RoundNullable(maxDown.Low);
            record.MinTime = maxDown.Time;
            record.ExtremumOrder = GetExtremumOrder(record.MinTime, record.MaxTime);
            record.MinutesFromMinToMax = DiffMinutes(record.MinTime, record.MaxTime);
            record.MinutesFromEntryToMax = DiffMinutes(record.EntryTime, record.MaxTime);
            record.MinutesFromEntryToMin = DiffMinutes(record.EntryTime, record.MinTime);
            record.PostMaxDrawdownPct = RoundNullable(CalculatePostMaxDrawdownPct(maxUp, afterEntry));
            FillExitMissStats(record, maxUp.High);
            ApplyUnambiguousOutcomeCorrection(record);
        }

        private static void FillBeforeEntryStats(
            CandidateEvaluationResult record,
            List<Candle> ordered,
            DateTime entryTime)
        {
            var beforeEntry = ordered
                .Where(x => x.Time <= entryTime)
                .OrderBy(x => x.Time)
                .ToList();

            if (beforeEntry.Count == 0 || record.ScanPrice <= 0m)
                return;

            var maxBeforeEntry = beforeEntry
                .OrderByDescending(x => x.High)
                .ThenBy(x => x.Time)
                .First();

            var minBeforeEntry = beforeEntry
                .OrderBy(x => x.Low)
                .ThenBy(x => x.Time)
                .First();

            record.MaxPctBeforeEntry = RoundNullable(CalcPct(record.ScanPrice, maxBeforeEntry.High));
            record.MaxPriceBeforeEntry = RoundNullable(maxBeforeEntry.High);
            record.MaxTimeBeforeEntry = maxBeforeEntry.Time;
            record.MinPctBeforeEntry = RoundNullable(CalcPct(record.ScanPrice, minBeforeEntry.Low));
            record.MinPriceBeforeEntry = RoundNullable(minBeforeEntry.Low);
            record.MinTimeBeforeEntry = minBeforeEntry.Time;

            var entryUndercutAbs = Math.Max(record.EntryPrice - minBeforeEntry.Low, 0m);
            record.EntryUndercutBeforeEntryAbs = RoundNullable(entryUndercutAbs);
            record.EntryUndercutBeforeEntryPct = record.EntryPrice > 0m
                ? RoundNullable((entryUndercutAbs / record.EntryPrice) * 100m)
                : null;
        }

        private static void FillExitMissStats(
            CandidateEvaluationResult record,
            decimal maxHighAfterEntry)
        {
            if (record.ExitTouched || record.ExitPrice <= 0m)
            {
                record.ExitMissAbs = null;
                record.ExitMissPct = null;
                record.NearTakeProfitMiss = false;
                return;
            }

            var missAbs = Math.Max(record.ExitPrice - maxHighAfterEntry, 0m);
            var missPct = record.ExitPrice > 0m
                ? (missAbs / record.ExitPrice) * 100m
                : 0m;

            record.ExitMissAbs = RoundNullable(missAbs);
            record.ExitMissPct = RoundNullable(missPct);
            record.NearTakeProfitMiss = missAbs > 0m && (missAbs <= 0.01m || missPct <= 0.1m);
        }

        private static decimal? CalculatePostMaxDrawdownPct(Candle maxUp, List<Candle> afterEntry)
        {
            if (maxUp.High <= 0m)
                return null;

            var afterMax = afterEntry
                .Where(x => x.Time >= maxUp.Time)
                .OrderBy(x => x.Time)
                .ToList();

            if (afterMax.Count == 0)
                return null;

            var minLowAfterMax = afterMax.Min(x => x.Low);
            return ((maxUp.High - minLowAfterMax) / maxUp.High) * 100m;
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

        private static void ApplyUnambiguousOutcomeCorrection(CandidateEvaluationResult record)
        {
            if (!record.EntryTouched)
                return;

            var exitWasReachable = record.MaxPrice.HasValue &&
                                   record.ExitPrice > 0m &&
                                   record.MaxPrice.Value >= record.ExitPrice;
            var stopWasReachable = record.MinPrice.HasValue &&
                                   record.StopLoss > 0m &&
                                   record.MinPrice.Value <= record.StopLoss;

            if (exitWasReachable && !stopWasReachable)
            {
                record.ExitTouched = true;
                record.StopTouched = false;
                record.ExitBeforeStop = true;
                record.StopBeforeExit = false;
                record.ExitTime ??= record.MaxTime;
                record.RealizedPct = RoundNullable(CalcPct(record.EntryPrice, record.ExitPrice));
                record.Outcome = "Win";
                return;
            }

            if (stopWasReachable && !exitWasReachable)
            {
                record.ExitTouched = false;
                record.StopTouched = true;
                record.ExitBeforeStop = false;
                record.StopBeforeExit = true;
                record.StopTime ??= record.MinTime;
                record.RealizedPct = RoundNullable(CalcPct(record.EntryPrice, record.StopLoss));
                record.Outcome = "Loss";
            }
        }

        private static decimal Round(decimal value)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal? RoundNullable(decimal? value)
        {
            return value.HasValue ? Round(value.Value) : null;
        }
    }
}
