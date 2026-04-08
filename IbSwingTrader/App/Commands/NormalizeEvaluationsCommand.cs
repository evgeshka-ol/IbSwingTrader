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

            record.MaxUpPct = RoundNullable(CalcPct(record.EntryPrice, maxUp.High));
            record.MaxUpTime = maxUp.Time;
            record.MaxDownPct = RoundNullable(CalcPct(record.EntryPrice, maxDown.Low));
            record.MaxDownTime = maxDown.Time;
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

        private static decimal Round(decimal value)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static decimal? RoundNullable(decimal value)
        {
            return Round(value);
        }
    }
}
