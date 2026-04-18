using IbSwingTrader.Domain.Dataset;

namespace IbSwingTrader.App.Commands
{
    public class BuildEvaluationDatasetCommand(
        ICandidateEvaluationCsvService evaluationCsvService,
        IJsonFileService jsonFileService,
        IHistoricalCache historicalCache,
        ICsvWriter csvWriter,
        IAgentPathService pathService,
        ITextLogger logger) : ICommand
    {
        private const decimal MinInterestingAmplitudePct = 5m;
        private const int MaxTradeDaysToMaxUpFromScan = 1;
        private const int ProgressLogInterval = 250;

        private readonly ICandidateEvaluationCsvService _evaluationCsvService = evaluationCsvService;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly IHistoricalCache _historicalCache = historicalCache;
        private readonly ICsvWriter _csvWriter = csvWriter;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITextLogger _logger = logger;

        public async Task RunAsync()
        {
            var evaluationsPath = _pathService.GetEvaluationsFile();
            var outputPath = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), "datasets", "evaluation-dataset.csv"));

            _logger.Info("Building evaluation dataset...");
            _logger.Info($"Evaluations source: {evaluationsPath}");

            var evaluations = await _evaluationCsvService.ReadAsync(evaluationsPath);

            if (evaluations.Count == 0)
            {
                _logger.Error("No evaluations found.");
                return;
            }

            _logger.Info($"Evaluations loaded: {evaluations.Count}");

            var activeCandidates = await LoadCurrentCandidatesAsync();
            var candidateIndex = activeCandidates
                .GroupBy(BuildCandidateKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x
                        .OrderByDescending(c => c.Score.Score)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var rows = new List<EvaluationDatasetRow>();

            for (var i = 0; i < evaluations.Count; i++)
            {
                var evaluation = evaluations[i];
                rows.Add(BuildRow(evaluation, candidateIndex));

                var processed = i + 1;
                if (processed == 1 ||
                    processed == evaluations.Count ||
                    processed % ProgressLogInterval == 0)
                {
                    _logger.Info($"Evaluation dataset progress: {processed}/{evaluations.Count}");
                }
            }

            rows = rows
                .GroupBy(x => BuildDatasetKey(x), StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderByDescending(r => r.AmplitudePct)
                    .ThenByDescending(r => r.PositivePotentialPct)
                    .ThenByDescending(r => r.EvaluatedAtMarketTime)
                    .First())
                .OrderBy(GetGroupPriority)
                .ThenByDescending(x => x.AmplitudePct)
                .ThenByDescending(x => x.PositivePotentialPct)
                .ThenByDescending(x => x.ScanTimeMarket)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _csvWriter.Write(outputPath, rows);

            _logger.Info($"Evaluation rows: {evaluations.Count}");
            _logger.Info($"Rows with active candidate snapshot: {rows.Count(x => x.HasActiveCandidateSnapshot)}");
            _logger.Info($"Evaluation dataset saved: {outputPath}");
            _logger.Info($"Group TradeCandidate: {rows.Count(x => x.GroupLabel == "TradeCandidate")}");
            _logger.Info($"Group Wishlist: {rows.Count(x => x.GroupLabel == "Wishlist")}");
            _logger.Info($"Group FilterReference: {rows.Count(x => x.GroupLabel == "FilterReference")}");
        }

        private async Task<List<CandidateDetails>> LoadCurrentCandidatesAsync()
        {
            var path = _pathService.GetCandidatesFile();
            if (!File.Exists(path))
                return [];

            var json = await File.ReadAllTextAsync(path);
            var first = json.FirstOrDefault(x => !char.IsWhiteSpace(x));

            if (first == '[')
                return await _jsonFileService.ReadAsync<List<CandidateDetails>>(path) ?? [];

            var document = await _jsonFileService.ReadAsync<CandidateFileDocument>(path);
            return document?.Candidates ?? [];
        }

        private EvaluationDatasetRow BuildRow(
            CandidateEvaluationResult evaluation,
            IReadOnlyDictionary<string, CandidateDetails> candidateIndex)
        {
            var key = BuildEvaluationKey(evaluation);
            candidateIndex.TryGetValue(key, out var candidate);
            var isFromWishlist = candidate?.IsFromWishlist ?? evaluation.IsFromWishlist;
            var cacheMetrics = TryBuildCacheMetrics(evaluation);

            var maxPct = cacheMetrics?.MaxPct ?? evaluation.MaxPct;
            var maxTime = cacheMetrics?.MaxTime ?? evaluation.MaxTime;
            var minPct = cacheMetrics?.MinPct ?? evaluation.MinPct;
            var minTime = cacheMetrics?.MinTime ?? evaluation.MinTime;
            var maxPctBeforeEntry = cacheMetrics?.MaxPctBeforeEntry ?? evaluation.MaxPctBeforeEntry;
            var maxTimeBeforeEntry = cacheMetrics?.MaxTimeBeforeEntry ?? evaluation.MaxTimeBeforeEntry;
            var minPctBeforeEntry = cacheMetrics?.MinPctBeforeEntry ?? evaluation.MinPctBeforeEntry;
            var minTimeBeforeEntry = cacheMetrics?.MinTimeBeforeEntry ?? evaluation.MinTimeBeforeEntry;
            var entryUndercutBeforeEntryAbs = cacheMetrics?.EntryUndercutBeforeEntryAbs ?? evaluation.EntryUndercutBeforeEntryAbs;
            var entryUndercutBeforeEntryPct = cacheMetrics?.EntryUndercutBeforeEntryPct ?? evaluation.EntryUndercutBeforeEntryPct;
            var exitMissAbs = cacheMetrics?.ExitMissAbs ?? evaluation.ExitMissAbs;
            var exitMissPct = cacheMetrics?.ExitMissPct ?? evaluation.ExitMissPct;
            var nearTakeProfitMiss = cacheMetrics?.NearTakeProfitMiss ?? evaluation.NearTakeProfitMiss;
            var postMaxDrawdownPct = cacheMetrics?.PostMaxDrawdownPct ?? evaluation.PostMaxDrawdownPct;
            var extremumOrder = cacheMetrics?.ExtremumOrder ?? evaluation.ExtremumOrder ?? string.Empty;
            var minutesFromMinToMax = cacheMetrics?.MinutesFromMinToMax ?? evaluation.MinutesFromMinToMax;
            var minutesFromEntryToMax = cacheMetrics?.MinutesFromEntryToMax ?? evaluation.MinutesFromEntryToMax;
            var minutesFromEntryToMin = cacheMetrics?.MinutesFromEntryToMin ?? evaluation.MinutesFromEntryToMin;

            var positivePotentialPct = Round(Math.Max(maxPct ?? 0m, 0m));
            var negativePotentialPct = Round(Math.Abs(Math.Min(minPct ?? 0m, 0m)));
            var amplitudePct = Round(CalculateAmplitudePct(
                evaluation.ScanPrice,
                evaluation.EntryPrice,
                maxPct,
                maxTime,
                minPct,
                minTime));

            var daysToMaxUpFromScan = DiffDays(evaluation.ScanTimeMarket, maxTime);
            var daysToMaxUpFromEntry = DiffDays(evaluation.EntryTime, maxTime);
            var daysToMaxDownFromScan = DiffDays(evaluation.ScanTimeMarket, minTime);
            var daysToMaxDownFromEntry = DiffDays(evaluation.EntryTime, minTime);

            return new EvaluationDatasetRow
            {
                Ticker = evaluation.Ticker,
                ScanTimeMarket = evaluation.ScanTimeMarket,
                PresetScanCode = evaluation.PresetScanCode,
                IsFromWishlist = isFromWishlist,
                Outcome = evaluation.Outcome ?? string.Empty,
                StrategyVersion = evaluation.StrategyVersion,
                EvaluatedAtMarketTime = evaluation.EvaluatedAtMarketTime,
                EntryTime = evaluation.EntryTime,
                DaysAfterEntry = evaluation.DaysAfterEntry,
                EntryPrice = evaluation.EntryPrice,
                ExitPrice = evaluation.ExitPrice,
                StopLoss = evaluation.StopLoss,
                ScanPrice = evaluation.ScanPrice,
                PlannedProfitPct = Round(CalcPctOrZero(evaluation.EntryPrice, evaluation.ExitPrice)),
                PlannedLossPct = Round(CalcPctOrZero(evaluation.EntryPrice, evaluation.StopLoss)),
                ScanMovePct = evaluation.ScanMovePct,
                CurrentPct = evaluation.CurrentPct,
                EntryDistanceToMinAfterScanPct = evaluation.EntryDistanceToMinAfterScanPct,
                MaxPct = maxPct,
                MaxTime = maxTime,
                MinPct = minPct,
                MinTime = minTime,
                MaxPctBeforeEntry = maxPctBeforeEntry,
                MaxTimeBeforeEntry = maxTimeBeforeEntry,
                MinPctBeforeEntry = minPctBeforeEntry,
                MinTimeBeforeEntry = minTimeBeforeEntry,
                EntryUndercutBeforeEntryAbs = entryUndercutBeforeEntryAbs,
                EntryUndercutBeforeEntryPct = entryUndercutBeforeEntryPct,
                PositivePotentialPct = positivePotentialPct,
                NegativePotentialPct = negativePotentialPct,
                AmplitudePct = amplitudePct,
                DaysToMaxUpFromScan = daysToMaxUpFromScan,
                DaysToMaxUpFromEntry = daysToMaxUpFromEntry,
                DaysToMaxDownFromScan = daysToMaxDownFromScan,
                DaysToMaxDownFromEntry = daysToMaxDownFromEntry,
                ExitMissAbs = exitMissAbs,
                ExitMissPct = exitMissPct,
                NearTakeProfitMiss = nearTakeProfitMiss,
                PostMaxDrawdownPct = postMaxDrawdownPct,
                ExtremumOrder = extremumOrder,
                MinutesFromMinToMax = minutesFromMinToMax,
                MinutesFromEntryToMax = minutesFromEntryToMax,
                MinutesFromEntryToMin = minutesFromEntryToMin,
                MaxDownBeforeMaxUp = CompareTimes(minTime, maxTime),
                GroupLabel = Classify(amplitudePct, daysToMaxUpFromScan),
                HasActiveCandidateSnapshot = candidate != null,
                CandidateScore = candidate?.Score.Score,
                WeeklyScore = candidate?.Score.WeeklyScore,
                DailyScore = candidate?.Score.DailyScore,
                EntryScore = candidate?.Score.EntryScore,
                DistanceTo20dHigh = candidate?.Context.DistanceTo20dHigh,
                DistanceTo52wHigh = candidate?.Context.DistanceTo52wHigh,
                DailyRsi14 = candidate?.Context.DailyRSI14,
                Pullback10d = candidate?.Diagnostics?.Pullback10d,
                DailyPullback10d = candidate?.Diagnostics?.DailyPullback10d,
                VolumeRatio20 = candidate?.Diagnostics?.VolumeRatio20,
                AtrRatio = candidate?.Diagnostics?.ATRRatio,
                TrendPosition = candidate?.Diagnostics?.TrendPosition,
                DailyTrendPosition = candidate?.Diagnostics?.DailyTrendPosition,
                BbMidSignedDistancePct = candidate?.Diagnostics?.BBMidSignedDistancePct,
                WeeklyMacdHistDelta = candidate?.Diagnostics?.WeeklyMACDHistDelta
            };
        }

        private CacheMetrics? TryBuildCacheMetrics(CandidateEvaluationResult evaluation)
        {
            if (!_historicalCache.TryLoad(evaluation.Ticker, Timeframe.M5, out var cached) ||
                cached == null ||
                cached.Count == 0)
            {
                return null;
            }

            var evaluationEnd = evaluation.EvaluationEndTime ?? evaluation.EvaluatedAtMarketTime;
            if (evaluationEnd <= evaluation.ScanTimeMarket)
                return null;

            var ordered = cached
                .Where(x => x.Time >= evaluation.ScanTimeMarket && x.Time <= evaluationEnd)
                .OrderBy(x => x.Time)
                .ToList();

            if (ordered.Count == 0 || !evaluation.EntryTouched || !evaluation.EntryTime.HasValue)
                return null;

            var beforeEntry = ordered
                .Where(x => x.Time <= evaluation.EntryTime.Value)
                .OrderBy(x => x.Time)
                .ToList();

            var afterEntry = ordered
                .Where(x => x.Time >= evaluation.EntryTime.Value)
                .OrderBy(x => x.Time)
                .ToList();

            if (afterEntry.Count == 0)
                return null;

            var maxAfterEntry = afterEntry
                .OrderByDescending(x => x.High)
                .ThenBy(x => x.Time)
                .First();

            var minAfterEntry = afterEntry
                .OrderBy(x => x.Low)
                .ThenBy(x => x.Time)
                .First();

            decimal? maxPctBeforeEntry = null;
            DateTime? maxTimeBeforeEntry = null;
            decimal? minPctBeforeEntry = null;
            DateTime? minTimeBeforeEntry = null;
            decimal? entryUndercutBeforeEntryAbs = null;
            decimal? entryUndercutBeforeEntryPct = null;

            if (beforeEntry.Count > 0 && evaluation.ScanPrice > 0m)
            {
                var maxBeforeEntry = beforeEntry
                    .OrderByDescending(x => x.High)
                    .ThenBy(x => x.Time)
                    .First();

                var minBeforeEntry = beforeEntry
                    .OrderBy(x => x.Low)
                    .ThenBy(x => x.Time)
                    .First();

                maxPctBeforeEntry = Round(CalcPct(evaluation.ScanPrice, maxBeforeEntry.High));
                maxTimeBeforeEntry = maxBeforeEntry.Time;
                minPctBeforeEntry = Round(CalcPct(evaluation.ScanPrice, minBeforeEntry.Low));
                minTimeBeforeEntry = minBeforeEntry.Time;

                var undercutAbs = Math.Max(evaluation.EntryPrice - minBeforeEntry.Low, 0m);
                entryUndercutBeforeEntryAbs = Round(undercutAbs);
                entryUndercutBeforeEntryPct = evaluation.EntryPrice > 0m
                    ? Round((undercutAbs / evaluation.EntryPrice) * 100m)
                    : null;
            }

            var maxPct = evaluation.ScanPrice > 0m
                ? Round(CalcPct(evaluation.ScanPrice, maxAfterEntry.High))
                : (decimal?)null;

            var minPct = evaluation.ScanPrice > 0m
                ? Round(CalcPct(evaluation.ScanPrice, minAfterEntry.Low))
                : (decimal?)null;

            var postMaxDrawdownPct = CalculatePostMaxDrawdownPct(maxAfterEntry, afterEntry);
            var (exitMissAbs, exitMissPct, nearTakeProfitMiss) = CalculateExitMiss(evaluation, maxAfterEntry.High);

            return new CacheMetrics
            {
                MaxPct = maxPct,
                MaxTime = maxAfterEntry.Time,
                MinPct = minPct,
                MinTime = minAfterEntry.Time,
                MaxPctBeforeEntry = maxPctBeforeEntry,
                MaxTimeBeforeEntry = maxTimeBeforeEntry,
                MinPctBeforeEntry = minPctBeforeEntry,
                MinTimeBeforeEntry = minTimeBeforeEntry,
                EntryUndercutBeforeEntryAbs = entryUndercutBeforeEntryAbs,
                EntryUndercutBeforeEntryPct = entryUndercutBeforeEntryPct,
                ExitMissAbs = exitMissAbs,
                ExitMissPct = exitMissPct,
                NearTakeProfitMiss = nearTakeProfitMiss,
                PostMaxDrawdownPct = postMaxDrawdownPct,
                ExtremumOrder = GetExtremumOrder(minAfterEntry.Time, maxAfterEntry.Time),
                MinutesFromMinToMax = DiffMinutes(minAfterEntry.Time, maxAfterEntry.Time),
                MinutesFromEntryToMax = DiffMinutes(evaluation.EntryTime, maxAfterEntry.Time),
                MinutesFromEntryToMin = DiffMinutes(evaluation.EntryTime, minAfterEntry.Time)
            };
        }

        private static string BuildEvaluationKey(CandidateEvaluationResult row)
        {
            return $"{row.Ticker}|{row.PresetScanCode}|{row.ScanTimeMarket:yyyy-MM-dd HH:mm:ss}";
        }

        private static string BuildCandidateKey(CandidateDetails row)
        {
            return $"{row.Ticker}|{row.Scan.PresetScanCode}|{row.Scan.ScanTimeMarket:yyyy-MM-dd HH:mm:ss}";
        }

        private static string BuildDatasetKey(EvaluationDatasetRow row)
        {
            return $"{row.Ticker}|{row.GroupLabel}";
        }

        private static int? DiffDays(DateTime from, DateTime? to)
        {
            if (!to.HasValue)
                return null;

            return (to.Value.Date - from.Date).Days;
        }

        private static int? DiffDays(DateTime? from, DateTime? to)
        {
            if (!from.HasValue || !to.HasValue)
                return null;

            return (to.Value.Date - from.Value.Date).Days;
        }

        private static bool? CompareTimes(DateTime? left, DateTime? right)
        {
            if (!left.HasValue || !right.HasValue)
                return null;

            return left.Value <= right.Value;
        }

        private static string Classify(
            decimal amplitudePct,
            int? daysToMaxUpFromScan)
        {
            if (!daysToMaxUpFromScan.HasValue ||
                daysToMaxUpFromScan.Value < 0 ||
                amplitudePct < MinInterestingAmplitudePct)
            {
                return "FilterReference";
            }

            return daysToMaxUpFromScan.Value <= MaxTradeDaysToMaxUpFromScan
                ? "TradeCandidate"
                : "Wishlist";
        }

        private static int GetGroupPriority(EvaluationDatasetRow row)
        {
            return row.GroupLabel switch
            {
                "TradeCandidate" => 0,
                "Wishlist" => 1,
                _ => 2
            };
        }

        private static decimal CalcPctOrZero(decimal from, decimal to)
        {
            if (from <= 0m)
                return 0m;

            return ((to - from) / from) * 100m;
        }

        private static decimal CalcPct(decimal from, decimal to)
        {
            if (from <= 0m)
                return 0m;

            return ((to - from) / from) * 100m;
        }

        private static decimal CalculateAmplitudePct(
            decimal scanPrice,
            decimal entryPrice,
            decimal? maxUpPct,
            DateTime? maxUpTime,
            decimal? maxDownPct,
            DateTime? maxDownTime)
        {
            var positivePotentialPct = Math.Max(maxUpPct ?? 0m, 0m);
            var negativePotentialPct = Math.Abs(Math.Min(maxDownPct ?? 0m, 0m));

            if (positivePotentialPct <= 0m)
                return 0m;

            if (maxUpTime.HasValue &&
                maxDownTime.HasValue &&
                maxUpTime.Value < maxDownTime.Value)
            {
                return positivePotentialPct;
            }

            return positivePotentialPct + negativePotentialPct;
        }

        private static decimal Round(decimal value)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static int? DiffMinutes(DateTime? from, DateTime? to)
        {
            if (!from.HasValue || !to.HasValue)
                return null;

            return (int)Math.Round((to.Value - from.Value).TotalMinutes, MidpointRounding.AwayFromZero);
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

        private static decimal? CalculatePostMaxDrawdownPct(Candle maxCandle, List<Candle> afterEntry)
        {
            if (maxCandle.High <= 0m)
                return null;

            var afterMax = afterEntry
                .Where(x => x.Time >= maxCandle.Time)
                .OrderBy(x => x.Time)
                .ToList();

            if (afterMax.Count == 0)
                return null;

            var minLowAfterMax = afterMax.Min(x => x.Low);
            return Round(((maxCandle.High - minLowAfterMax) / maxCandle.High) * 100m);
        }

        private static (decimal? ExitMissAbs, decimal? ExitMissPct, bool NearTakeProfitMiss) CalculateExitMiss(
            CandidateEvaluationResult evaluation,
            decimal maxHighAfterEntry)
        {
            if (evaluation.ExitTouched || evaluation.ExitPrice <= 0m)
                return (null, null, false);

            var missAbs = Math.Max(evaluation.ExitPrice - maxHighAfterEntry, 0m);
            var missPct = evaluation.ExitPrice > 0m
                ? (missAbs / evaluation.ExitPrice) * 100m
                : 0m;

            return (
                Round(missAbs),
                Round(missPct),
                missAbs > 0m && (missAbs <= 0.01m || missPct <= 0.1m));
        }

        private sealed class CacheMetrics
        {
            public decimal? MaxPct { get; init; }
            public DateTime? MaxTime { get; init; }
            public decimal? MinPct { get; init; }
            public DateTime? MinTime { get; init; }
            public decimal? MaxPctBeforeEntry { get; init; }
            public DateTime? MaxTimeBeforeEntry { get; init; }
            public decimal? MinPctBeforeEntry { get; init; }
            public DateTime? MinTimeBeforeEntry { get; init; }
            public decimal? EntryUndercutBeforeEntryAbs { get; init; }
            public decimal? EntryUndercutBeforeEntryPct { get; init; }
            public decimal? ExitMissAbs { get; init; }
            public decimal? ExitMissPct { get; init; }
            public bool NearTakeProfitMiss { get; init; }
            public decimal? PostMaxDrawdownPct { get; init; }
            public string ExtremumOrder { get; init; } = string.Empty;
            public int? MinutesFromMinToMax { get; init; }
            public int? MinutesFromEntryToMax { get; init; }
            public int? MinutesFromEntryToMin { get; init; }
        }
    }
}
