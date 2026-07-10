using IbSwingTrader.Abstractions.Evaluation;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Application.Dataset
{
    public class EvaluationDatasetBuilder(
        ICandidateEvaluationCsvService evaluationCsvService,
        IEvaluationDatasetCsvService evaluationDatasetCsvService,
        ICandidateFileService candidateFileService,
        IHistoricalCache historicalCache,
        IFeatureEngine featureEngine,
        IAgentPathService pathService,
        IBuildEvaluationDatasetSettingsProvider buildEvaluationDatasetSettingsProvider,
        ICandidatePatternVerdictService patternVerdictService,
        ITextLogger logger) : IEvaluationDatasetBuilder
    {
        private const decimal MinInterestingAmplitudePct = 5m;
        private const int MaxTradeDaysToMaxUpFromScan = 1;
        private const int ProgressLogInterval = 250;
        private const int RecentDailySeriesLength = 12;
        private const int RecentWeeklySeriesLength = 10;
        private const int RecentH4SeriesLength = 16;

        private readonly ICandidateEvaluationCsvService _evaluationCsvService = evaluationCsvService;
        private readonly IEvaluationDatasetCsvService _evaluationDatasetCsvService = evaluationDatasetCsvService;
        private readonly ICandidateFileService _candidateFileService = candidateFileService;
        private readonly IHistoricalCache _historicalCache = historicalCache;
        private readonly IFeatureEngine _featureEngine = featureEngine;
        private readonly IAgentPathService _pathService = pathService;
        private readonly IBuildEvaluationDatasetSettingsProvider _buildEvaluationDatasetSettingsProvider = buildEvaluationDatasetSettingsProvider;
        private readonly ICandidatePatternVerdictService _patternVerdictService = patternVerdictService;
        private readonly ITextLogger _logger = logger;

        public async Task<List<EvaluationDatasetRow>> ReadCurrentAsync()
        {
            var outputPath = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), "datasets", "evaluation-dataset.csv"));

            var rows = await _evaluationDatasetCsvService.ReadAsync(outputPath);
            return [.. rows
                .GroupBy(BuildDatasetKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderByDescending(r => r.EvaluatedAt)
                    .First())];
        }

        public async Task UpsertAsync(List<CandidateEvaluationResult> evaluations)
        {
            ArgumentNullException.ThrowIfNull(evaluations);

            var settings = _buildEvaluationDatasetSettingsProvider.Get();
            var outputPath = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), "datasets", "evaluation-dataset.csv"));

            var activeCandidates = await LoadCurrentCandidatesAsync();
            var candidateIndex = activeCandidates
                .GroupBy(x => BuildCandidateKey(x.Candidate), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x
                        .OrderBy(c => c.GroupPriority)
                        .ThenBy(c => c.DisplayRank)
                        .ThenByDescending(c => c.Candidate.Score.Score)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var existingRows = await ReadCurrentAsync();
            var rebuiltRows = evaluations
                .GroupBy(BuildEvaluationKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderByDescending(r => r.EvaluatedAt)
                    .ThenByDescending(r => r.EvaluationEndTime ?? DateTime.MinValue)
                    .First())
                .Select(x => BuildRow(x, candidateIndex))
                .ToList();

            var legacyBackfillRows = await BuildLegacyNoEntryZeroAmplitudeBackfillRowsAsync(
                settings,
                existingRows,
                candidateIndex);
            if (legacyBackfillRows.Count > 0)
            {
                rebuiltRows.AddRange(legacyBackfillRows);
            }

            var rebuildKeys = rebuiltRows
                .Select(BuildDatasetKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var mergedRows = existingRows
                .Where(x => !rebuildKeys.Contains(BuildDatasetKey(x)))
                .Concat(rebuiltRows)
                .GroupBy(BuildDatasetKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderByDescending(r => r.AmplitudePct)
                    .ThenByDescending(r => r.PositivePotentialPct)
                    .ThenByDescending(r => r.EvaluatedAt)
                    .First())
                .ToList();

            if (settings.RecentScanDays.HasValue && settings.RecentScanDays.Value > 0)
            {
                var recentCutoff = MarketTime.Now().Date.AddDays(-settings.RecentScanDays.Value);
                mergedRows = [.. mergedRows.Where(x => x.ScanTime >= recentCutoff)];
            }

            if (settings.MinScanTime.HasValue)
            {
                mergedRows = [.. mergedRows.Where(x => x.ScanTime >= settings.MinScanTime.Value)];
            }

            if (settings.MinAmplitudePct.HasValue)
            {
                mergedRows = [.. mergedRows.Where(x => x.AmplitudePct >= settings.MinAmplitudePct.Value)];
            }

            EnsureDerivedFields(mergedRows);
            mergedRows.Sort((left, right) => CompareRows(left, right, settings));
            await _evaluationDatasetCsvService.WriteAsync(outputPath, mergedRows);
        }

        public async Task RunAsync()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var settings = _buildEvaluationDatasetSettingsProvider.Get();
            var evaluationsPath = _pathService.GetEvaluationsFile();
            var evaluationsArchivePath = _pathService.GetEvaluationsArchiveFile();
            var outputPath = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), "datasets", "evaluation-dataset.csv"));

            _logger.Info("Building evaluation dataset...");
            _logger.Info($"Evaluations source: {evaluationsPath}");
            _logger.Info($"Evaluations archive source: {evaluationsArchivePath}");

            var evaluations = await _evaluationCsvService.ReadAsync(evaluationsPath);
            var archivedEvaluations = await _evaluationCsvService.ReadAsync(evaluationsArchivePath);
            evaluations.AddRange(archivedEvaluations);
            evaluations = evaluations
                .GroupBy(BuildEvaluationKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderByDescending(r => r.EvaluatedAt)
                    .ThenByDescending(r => r.EvaluationEndTime ?? DateTime.MinValue)
                    .First())
                .ToList();

            if (settings.RecentScanDays.HasValue && settings.RecentScanDays.Value > 0)
            {
                var recentCutoff = MarketTime.Now().Date.AddDays(-settings.RecentScanDays.Value);
                evaluations = evaluations
                    .Where(x => x.ScanTime >= recentCutoff)
                    .ToList();
            }

            if (settings.MinScanTime.HasValue)
            {
                evaluations = evaluations
                    .Where(x => x.ScanTime >= settings.MinScanTime.Value)
                    .ToList();
            }

            if (evaluations.Count == 0)
            {
                _logger.Error(
                    $"No evaluations found. " +
                    $"Elapsed={ElapsedTimeFormatter.Format(stopwatch.Elapsed)}");
                return;
            }

            _logger.Info($"Evaluations loaded: {evaluations.Count}");
            if (settings.RecentScanDays.HasValue)
                _logger.Info($"RecentScanDays filter: {settings.RecentScanDays.Value}");
            if (settings.BackfillLegacyNoEntryZeroAmplitudeDays.HasValue)
                _logger.Info($"BackfillLegacyNoEntryZeroAmplitudeDays: {settings.BackfillLegacyNoEntryZeroAmplitudeDays.Value}");
            if (settings.MinScanTime.HasValue)
                _logger.Info($"MinScanTime filter: {settings.MinScanTime.Value:yyyy-MM-dd HH:mm:ss}");
            if (settings.MinAmplitudePct.HasValue)
                _logger.Info($"MinAmplitudePct filter: {settings.MinAmplitudePct.Value:0.##}");

            var activeCandidates = await LoadCurrentCandidatesAsync();
            var candidateIndex = activeCandidates
                .GroupBy(x => BuildCandidateKey(x.Candidate), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x
                        .OrderBy(c => c.GroupPriority)
                        .ThenBy(c => c.DisplayRank)
                        .ThenByDescending(c => c.Candidate.Score.Score)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var existingRows = await _evaluationDatasetCsvService.ReadAsync(outputPath);
            existingRows = [.. existingRows
                .GroupBy(BuildDatasetKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderByDescending(r => r.EvaluatedAt)
                    .First())];

            var existingRowIndex = existingRows.ToDictionary(
                BuildDatasetKey,
                x => x,
                StringComparer.OrdinalIgnoreCase);

            var evaluationKeys = evaluations
                .Select(BuildEvaluationKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rebuildEvaluations = evaluations
                .Where(x => ShouldRebuildRow(x, candidateIndex, existingRowIndex))
                .ToList();

            var legacyBackfillKeys = BuildLegacyNoEntryZeroAmplitudeKeys(settings, existingRowIndex);
            if (legacyBackfillKeys.Count > 0)
            {
                rebuildEvaluations = [.. rebuildEvaluations
                    .Concat(evaluations.Where(x => legacyBackfillKeys.Contains(BuildEvaluationKey(x))))
                    .GroupBy(BuildEvaluationKey, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x
                        .OrderByDescending(r => r.EvaluatedAt)
                        .ThenByDescending(r => r.EvaluationEndTime ?? DateTime.MinValue)
                        .First())];
            }

            var rebuildKeys = rebuildEvaluations
                .Select(BuildEvaluationKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rows = existingRows
                .Where(x => evaluationKeys.Contains(BuildDatasetKey(x)) &&
                            !rebuildKeys.Contains(BuildDatasetKey(x)))
                .ToList();

            _logger.Info($"Existing evaluation dataset rows loaded: {existingRows.Count}");
            _logger.Info($"Evaluation rows to rebuild: {rebuildEvaluations.Count}");
            _logger.Info($"Evaluation rows reused: {rows.Count}");

            for (var i = 0; i < rebuildEvaluations.Count; i++)
            {
                var evaluation = rebuildEvaluations[i];
                rows.Add(BuildRow(evaluation, candidateIndex));

                var processed = i + 1;
                if (processed == 1 ||
                    processed == rebuildEvaluations.Count ||
                    processed % ProgressLogInterval == 0)
                {
                    _logger.Info($"Evaluation dataset progress: {processed}/{rebuildEvaluations.Count}");
                }
            }

            rows = [.. rows
                .GroupBy(x => BuildDatasetKey(x), StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderByDescending(r => r.AmplitudePct)
                    .ThenByDescending(r => r.PositivePotentialPct)
                    .ThenByDescending(r => r.EvaluatedAt)
                    .First())];

            if (settings.MinAmplitudePct.HasValue)
            {
                rows = [.. rows.Where(x => x.AmplitudePct >= settings.MinAmplitudePct.Value)];
            }

            EnsureDerivedFields(rows);
            rows.Sort((left, right) => CompareRows(left, right, settings));

            await _evaluationDatasetCsvService.WriteAsync(outputPath, rows);

            _logger.Info($"Evaluation rows: {evaluations.Count}");
            _logger.Info($"Rows with active candidate snapshot: {rows.Count(x => x.HasActiveCandidateSnapshot)}");
            _logger.Info($"Evaluation dataset saved: {outputPath}");
            _logger.Info($"Group TradeCandidate: {rows.Count(x => x.GroupLabel == "TradeCandidate")}");
            _logger.Info($"Group Wishlist: {rows.Count(x => x.GroupLabel == "Wishlist")}");
            _logger.Info($"Group FilterReference: {rows.Count(x => x.GroupLabel == "FilterReference")}");
            _logger.Info(
                $"Evaluation dataset build completed. " +
                $"Evaluations={evaluations.Count}, " +
                $"Rows={rows.Count}, " +
                $"Elapsed={ElapsedTimeFormatter.Format(stopwatch.Elapsed)}");
        }

        private async Task<List<RankedCandidateSnapshot>> LoadCurrentCandidatesAsync()
        {
            var path = _pathService.GetCandidatesFile();
            var document = await _candidateFileService.ReadAsync(path);

            var primaryCandidates = document.Candidates
                .Select(x =>
                {
                    x.CandidateSource = string.IsNullOrWhiteSpace(x.CandidateSource) ? "Primary" : x.CandidateSource;
                    return x;
                });

            var sameDayCandidates = document.SameDayCandidates
                .Select(x =>
                {
                    if (string.IsNullOrWhiteSpace(x.CandidateSource) ||
                        x.CandidateSource.Equals("Primary", StringComparison.OrdinalIgnoreCase))
                    {
                        x.CandidateSource = "SameDayContinuation";
                    }

                    return x;
                });

            var snapshots = BuildRankedCandidateSnapshots("Runaway", sameDayCandidates)
                .Concat(BuildRankedCandidateSnapshots("Reversal", primaryCandidates));

            return snapshots
                .GroupBy(x => BuildCandidateKey(x.Candidate), StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderBy(y => y.GroupPriority)
                    .ThenBy(y => y.DisplayRank)
                    .ThenByDescending(y => y.Candidate.Score.Score)
                    .First())
                .ToList();
        }

        private EvaluationDatasetRow BuildRow(
            CandidateEvaluationResult evaluation,
            Dictionary<string, RankedCandidateSnapshot> candidateIndex)
        {
            var settings = _buildEvaluationDatasetSettingsProvider.Get();
            var key = BuildEvaluationKey(evaluation);
            candidateIndex.TryGetValue(key, out var candidateSnapshot);
            var candidate = candidateSnapshot?.Candidate;
            var isFromWishlist = candidate?.IsFromWishlist ?? evaluation.IsFromWishlist;
            var candidateSource = candidate?.CandidateSource ?? evaluation.CandidateSource;
            if (string.IsNullOrWhiteSpace(candidateSource))
                candidateSource = "Primary";
            var cacheMetrics = TryBuildCacheMetrics(evaluation, settings);
            var recentSeries = TryBuildRecentSeries(evaluation, settings);

            var maxPct = cacheMetrics?.MaxPct ?? evaluation.MaxPct;
            var maxPrice = cacheMetrics?.MaxPrice ?? evaluation.MaxPrice;
            var maxTime = cacheMetrics?.MaxTime ?? evaluation.MaxTime;
            var minPct = cacheMetrics?.MinPct ?? evaluation.MinPct;
            var minPrice = cacheMetrics?.MinPrice ?? evaluation.MinPrice;
            var minTime = cacheMetrics?.MinTime ?? evaluation.MinTime;
            var maxPctBeforeEntry = cacheMetrics?.MaxPctBeforeEntry ?? evaluation.MaxPctBeforeEntry;
            var maxPriceBeforeEntry = cacheMetrics?.MaxPriceBeforeEntry ?? evaluation.MaxPriceBeforeEntry;
            var maxTimeBeforeEntry = cacheMetrics?.MaxTimeBeforeEntry ?? evaluation.MaxTimeBeforeEntry;
            var minPctBeforeEntry = cacheMetrics?.MinPctBeforeEntry ?? evaluation.MinPctBeforeEntry;
            var minPriceBeforeEntry = cacheMetrics?.MinPriceBeforeEntry ?? evaluation.MinPriceBeforeEntry;
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
            var bestEntryDelayBarsM15 = cacheMetrics?.BestEntryDelayBarsM15 ?? evaluation.BestEntryDelayBarsM15;
            var bestEntryDelayBarsH1 = cacheMetrics?.BestEntryDelayBarsH1 ?? evaluation.BestEntryDelayBarsH1;
            var minBeforeMaxPct = cacheMetrics?.MinBeforeMaxPct ?? evaluation.MinBeforeMaxPct;
            var barsToMin = cacheMetrics?.BarsToMin ?? evaluation.BarsToMin;
            var barsToMax = cacheMetrics?.BarsToMax ?? evaluation.BarsToMax;
            var reachedTargetBeforeEntry = cacheMetrics?.ReachedTargetBeforeEntry ?? evaluation.ReachedTargetBeforeEntry;
            var entryMissReason = cacheMetrics?.EntryMissReason ?? evaluation.EntryMissReason ?? string.Empty;
            var optimalEntryDiscountPct = cacheMetrics?.OptimalEntryDiscountPct ?? evaluation.OptimalEntryDiscountPct;
            var adverseMoveBeforeRunPct = cacheMetrics?.AdverseMoveBeforeRunPct ?? evaluation.AdverseMoveBeforeRunPct;
            var minDepthGroup = GetMinDepthGroup(minPct);
            var maxStrengthGroup = GetMaxStrengthGroup(maxPct);
            var extremumSubgroup = BuildExtremumSubgroup(extremumOrder, minDepthGroup, maxStrengthGroup);

            var positivePotentialPct = Round(Math.Max(maxPct ?? 0m, 0m));
            var negativePotentialPct = Round(Math.Abs(Math.Min(minPct ?? 0m, 0m)));
            var amplitudePct = Round(CalculateAmplitudePct(
                evaluation.ScanPrice,
                maxPct,
                maxTime,
                minPct,
                minTime));

            var daysToMaxUpFromScan = DiffDays(evaluation.ScanTime, maxTime);
            var daysToMaxUpFromEntry = DiffDays(evaluation.EntryTime, maxTime);
            var daysToMaxDownFromScan = DiffDays(evaluation.ScanTime, minTime);
            var daysToMaxDownFromEntry = DiffDays(evaluation.EntryTime, minTime);

            var row = new EvaluationDatasetRow
            {
                Ticker = evaluation.Ticker,
                ScanTime = evaluation.ScanTime,
                EntryTime = evaluation.EntryTime,
                ExitTime = evaluation.ExitTime ??
                    (string.Equals(evaluation.Outcome, "Loss", StringComparison.OrdinalIgnoreCase)
                        ? evaluation.StopTime
                        : null),
                PresetScanCode = evaluation.PresetScanCode,
                IsFromWishlist = isFromWishlist,
                Outcome = evaluation.Outcome ?? string.Empty,
                StrategyVersion = evaluation.StrategyVersion,
                EvaluatedAt = evaluation.EvaluatedAt,
                IsStaleOpen = evaluation.IsStaleOpen,
                OpenAgeDays = evaluation.OpenAgeDays,
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
                MaxPrice = maxPrice,
                MaxTime = maxTime,
                MinPct = minPct,
                MinPrice = minPrice,
                MinTime = minTime,
                MaxPctBeforeEntry = maxPctBeforeEntry,
                MaxPriceBeforeEntry = maxPriceBeforeEntry,
                MaxTimeBeforeEntry = maxTimeBeforeEntry,
                MinPctBeforeEntry = minPctBeforeEntry,
                MinPriceBeforeEntry = minPriceBeforeEntry,
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
                MinDepthGroup = minDepthGroup,
                MaxStrengthGroup = maxStrengthGroup,
                ExtremumSubgroup = extremumSubgroup,
                MinutesFromMinToMax = minutesFromMinToMax,
                MinutesFromEntryToMax = minutesFromEntryToMax,
                MinutesFromEntryToMin = minutesFromEntryToMin,
                BestEntryDelayBarsM15 = bestEntryDelayBarsM15,
                BestEntryDelayBarsH1 = bestEntryDelayBarsH1,
                MinBeforeMaxPct = minBeforeMaxPct,
                BarsToMin = barsToMin,
                BarsToMax = barsToMax,
                ReachedTargetBeforeEntry = reachedTargetBeforeEntry,
                EntryMissReason = entryMissReason,
                OptimalEntryDiscountPct = optimalEntryDiscountPct,
                AdverseMoveBeforeRunPct = adverseMoveBeforeRunPct,
                MaxDownBeforeMaxUp = CompareTimes(minTime, maxTime),
                GroupLabel = Classify(amplitudePct, daysToMaxUpFromScan),
                CandidateSource = candidateSource,
                DetectedPipeline = evaluation.DetectedPipeline,
                DetectedPattern = evaluation.DetectedPattern,
                PatternVerdict = evaluation.PatternVerdict,
                PatternVerdictReason = evaluation.PatternVerdictReason,
                CandidateGroup = NormalizeCandidateGroup(candidateSnapshot?.GroupName),
                CandidateDisplayRank = candidateSnapshot?.DisplayRank,
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
                WeeklyMacdHistDelta = candidate?.Diagnostics?.WeeklyMACDHistDelta,
                RecentDailyBbUpperBandSeries = ResolveSeries(candidate?.RecentDailyBbUpperBandSeries, evaluation.RecentDailyBbUpperBandSeries, recentSeries?.DailyBbUpperBandSeries),
                RecentDailyBbMidBandSeries = ResolveSeries(candidate?.RecentDailyBbMidBandSeries, evaluation.RecentDailyBbMidBandSeries, recentSeries?.DailyBbMidBandSeries),
                RecentDailyBbLowerBandSeries = ResolveSeries(candidate?.RecentDailyBbLowerBandSeries, evaluation.RecentDailyBbLowerBandSeries, recentSeries?.DailyBbLowerBandSeries),
                RecentDailyRsiSeries = ResolveSeries(candidate?.RecentDailyRsiSeries, evaluation.RecentDailyRsiSeries, recentSeries?.DailyRsiSeries),
                RecentDailyMacdLineSeries = ResolveSeries(candidate?.RecentDailyMacdLineSeries, evaluation.RecentDailyMacdLineSeries, recentSeries?.DailyMacdLineSeries),
                RecentDailyMacdSignalSeries = ResolveSeries(candidate?.RecentDailyMacdSignalSeries, evaluation.RecentDailyMacdSignalSeries, recentSeries?.DailyMacdSignalSeries),
                RecentDailyMacdHistogramSeries = ResolveSeries(candidate?.RecentDailyMacdHistogramSeries, evaluation.RecentDailyMacdHistogramSeries, recentSeries?.DailyMacdHistogramSeries),
                RecentWeeklyBbUpperBandSeries = ResolveSeries(candidate?.RecentWeeklyBbUpperBandSeries, evaluation.RecentWeeklyBbUpperBandSeries, recentSeries?.WeeklyBbUpperBandSeries),
                RecentWeeklyBbMidBandSeries = ResolveSeries(candidate?.RecentWeeklyBbMidBandSeries, evaluation.RecentWeeklyBbMidBandSeries, recentSeries?.WeeklyBbMidBandSeries),
                RecentWeeklyBbLowerBandSeries = ResolveSeries(candidate?.RecentWeeklyBbLowerBandSeries, evaluation.RecentWeeklyBbLowerBandSeries, recentSeries?.WeeklyBbLowerBandSeries),
                RecentWeeklyRsiSeries = ResolveSeries(candidate?.RecentWeeklyRsiSeries, evaluation.RecentWeeklyRsiSeries, recentSeries?.WeeklyRsiSeries),
                RecentWeeklyMacdLineSeries = ResolveSeries(candidate?.RecentWeeklyMacdLineSeries, evaluation.RecentWeeklyMacdLineSeries, recentSeries?.WeeklyMacdLineSeries),
                RecentWeeklyMacdSignalSeries = ResolveSeries(candidate?.RecentWeeklyMacdSignalSeries, evaluation.RecentWeeklyMacdSignalSeries, recentSeries?.WeeklyMacdSignalSeries),
                RecentWeeklyMacdHistogramSeries = ResolveSeries(candidate?.RecentWeeklyMacdHistogramSeries, evaluation.RecentWeeklyMacdHistogramSeries, recentSeries?.WeeklyMacdHistogramSeries),
                RecentH4BbUpperBandSeries = ResolveSeries(candidate?.RecentH4BbUpperBandSeries, evaluation.RecentH4BbUpperBandSeries, recentSeries?.H4BbUpperBandSeries),
                RecentH4BbMidBandSeries = ResolveSeries(candidate?.RecentH4BbMidBandSeries, evaluation.RecentH4BbMidBandSeries, recentSeries?.H4BbMidBandSeries),
                RecentH4BbLowerBandSeries = ResolveSeries(candidate?.RecentH4BbLowerBandSeries, evaluation.RecentH4BbLowerBandSeries, recentSeries?.H4BbLowerBandSeries),
                RecentH4RsiSeries = ResolveSeries(candidate?.RecentH4RsiSeries, evaluation.RecentH4RsiSeries, recentSeries?.H4RsiSeries),
                RecentH4MacdLineSeries = ResolveSeries(candidate?.RecentH4MacdLineSeries, evaluation.RecentH4MacdLineSeries, recentSeries?.H4MacdLineSeries),
                RecentH4MacdSignalSeries = ResolveSeries(candidate?.RecentH4MacdSignalSeries, evaluation.RecentH4MacdSignalSeries, recentSeries?.H4MacdSignalSeries),
                RecentH4MacdHistogramSeries = ResolveSeries(candidate?.RecentH4MacdHistogramSeries, evaluation.RecentH4MacdHistogramSeries, recentSeries?.H4MacdHistogramSeries)
            };

            var verdict = ResolvePatternVerdict(row);
            row.DetectedPipeline = verdict.DetectedPipeline;
            row.DetectedPattern = verdict.DetectedPattern;
            row.PatternVerdict = verdict.PatternVerdict;
            row.PatternVerdictReason = verdict.PatternVerdictReason;

            return row;
        }

        private CandidatePatternVerdict ResolvePatternVerdict(EvaluationDatasetRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.PatternVerdict) &&
                !string.IsNullOrWhiteSpace(row.DetectedPipeline) &&
                !string.IsNullOrWhiteSpace(row.DetectedPattern) &&
                row.PatternVerdict != "Unknown")
            {
                return new CandidatePatternVerdict(
                    row.DetectedPipeline,
                    row.DetectedPattern,
                    row.PatternVerdict,
                    row.PatternVerdictReason);
            }

            return _patternVerdictService.Analyze(row);
        }

        private void EnsureDerivedFields(IEnumerable<EvaluationDatasetRow> rows)
        {
            foreach (var row in rows)
            {
                var verdict = ResolvePatternVerdict(row);
                row.DetectedPipeline = verdict.DetectedPipeline;
                row.DetectedPattern = verdict.DetectedPattern;
                row.PatternVerdict = verdict.PatternVerdict;
                row.PatternVerdictReason = verdict.PatternVerdictReason;
            }
        }

        private CacheMetrics? TryBuildCacheMetrics(
            CandidateEvaluationResult evaluation,
            BuildEvaluationDatasetSettings settings)
        {
            if (settings.SkipCacheMetricsRebuildWhenPresent &&
                HasEvaluationCacheMetrics(evaluation))
            {
                return null;
            }

            if (!_historicalCache.TryLoad(evaluation.Ticker, Timeframe.M5, out var cached) ||
                cached == null ||
                cached.Count == 0)
            {
                return null;
            }

            var evaluationEnd = evaluation.EvaluationEndTime ?? evaluation.EvaluatedAt;
            if (evaluationEnd <= evaluation.ScanTime)
                return null;

            var ordered = cached
                .Where(x => x.Time >= evaluation.ScanTime && x.Time <= evaluationEnd)
                .OrderBy(x => x.Time)
                .ToList();

            if (ordered.Count == 0)
                return null;

            var scanWindow = ordered;
            var hasEntry = evaluation.EntryTouched && evaluation.EntryTime.HasValue;
            var afterEntry = hasEntry
                ? ordered
                    .Where(x => x.Time >= evaluation.EntryTime!.Value)
                    .OrderBy(x => x.Time)
                    .ToList()
                : [];
            var beforeEntry = hasEntry
                ? ordered
                    .Where(x => x.Time <= evaluation.EntryTime!.Value)
                    .OrderBy(x => x.Time)
                    .ToList()
                : [];

            var extremumWindow = hasEntry && afterEntry.Count > 0
                ? afterEntry
                : scanWindow;

            var maxAfterScan = extremumWindow
                .OrderByDescending(x => x.High)
                .ThenBy(x => x.Time)
                .First();

            var minAfterScan = extremumWindow
                .OrderBy(x => x.Low)
                .ThenBy(x => x.Time)
                .First();

            decimal? maxPctBeforeEntry = null;
            decimal? maxPriceBeforeEntry = null;
            DateTime? maxTimeBeforeEntry = null;
            decimal? minPctBeforeEntry = null;
            decimal? minPriceBeforeEntry = null;
            DateTime? minTimeBeforeEntry = null;
            decimal? entryUndercutBeforeEntryAbs = null;
            decimal? entryUndercutBeforeEntryPct = null;

            if (hasEntry && beforeEntry.Count > 0 && evaluation.ScanPrice > 0m)
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
                maxPriceBeforeEntry = Round(maxBeforeEntry.High);
                maxTimeBeforeEntry = maxBeforeEntry.Time;
                minPctBeforeEntry = Round(CalcPct(evaluation.ScanPrice, minBeforeEntry.Low));
                minPriceBeforeEntry = Round(minBeforeEntry.Low);
                minTimeBeforeEntry = minBeforeEntry.Time;

                var undercutAbs = Math.Max(evaluation.EntryPrice - minBeforeEntry.Low, 0m);
                entryUndercutBeforeEntryAbs = Round(undercutAbs);
                entryUndercutBeforeEntryPct = evaluation.EntryPrice > 0m
                    ? Round((undercutAbs / evaluation.EntryPrice) * 100m)
                    : null;
            }

            var maxPct = evaluation.ScanPrice > 0m
                ? Round(CalcPct(evaluation.ScanPrice, maxAfterScan.High))
                : (decimal?)null;
            var maxPrice = Round(maxAfterScan.High);

            var minPct = evaluation.ScanPrice > 0m
                ? Round(CalcPct(evaluation.ScanPrice, minAfterScan.Low))
                : (decimal?)null;
            var minPrice = Round(minAfterScan.Low);

            var postMaxDrawdownPct = hasEntry && afterEntry.Count > 0
                ? CalculatePostMaxDrawdownPct(maxAfterScan, afterEntry)
                : CalculatePostMaxDrawdownPct(maxAfterScan, scanWindow);
            var (exitMissAbs, exitMissPct, nearTakeProfitMiss) = hasEntry
                ? CalculateExitMiss(evaluation, maxAfterScan.High)
                : (null, null, false);
            var entryTiming = CalculateEntryTimingMetrics(
                scanWindow,
                evaluation.ScanPrice,
                evaluation.EntryPrice,
                evaluation.ExitPrice,
                hasEntry);

            return new CacheMetrics
            {
                MaxPct = maxPct,
                MaxPrice = maxPrice,
                MaxTime = maxAfterScan.Time,
                MinPct = minPct,
                MinPrice = minPrice,
                MinTime = minAfterScan.Time,
                MaxPctBeforeEntry = maxPctBeforeEntry,
                MaxPriceBeforeEntry = maxPriceBeforeEntry,
                MaxTimeBeforeEntry = maxTimeBeforeEntry,
                MinPctBeforeEntry = minPctBeforeEntry,
                MinPriceBeforeEntry = minPriceBeforeEntry,
                MinTimeBeforeEntry = minTimeBeforeEntry,
                EntryUndercutBeforeEntryAbs = entryUndercutBeforeEntryAbs,
                EntryUndercutBeforeEntryPct = entryUndercutBeforeEntryPct,
                ExitMissAbs = exitMissAbs,
                ExitMissPct = exitMissPct,
                NearTakeProfitMiss = nearTakeProfitMiss,
                PostMaxDrawdownPct = postMaxDrawdownPct,
                ExtremumOrder = GetExtremumOrder(minAfterScan.Time, maxAfterScan.Time),
                MinutesFromMinToMax = DiffMinutes(minAfterScan.Time, maxAfterScan.Time),
                MinutesFromEntryToMax = hasEntry ? DiffMinutes(evaluation.EntryTime, maxAfterScan.Time) : null,
                MinutesFromEntryToMin = hasEntry ? DiffMinutes(evaluation.EntryTime, minAfterScan.Time) : null,
                BestEntryDelayBarsM15 = entryTiming.BestEntryDelayBarsM15,
                BestEntryDelayBarsH1 = entryTiming.BestEntryDelayBarsH1,
                MinBeforeMaxPct = entryTiming.MinBeforeMaxPct,
                BarsToMin = entryTiming.BarsToMin,
                BarsToMax = entryTiming.BarsToMax,
                ReachedTargetBeforeEntry = entryTiming.ReachedTargetBeforeEntry,
                EntryMissReason = entryTiming.EntryMissReason,
                OptimalEntryDiscountPct = entryTiming.OptimalEntryDiscountPct,
                AdverseMoveBeforeRunPct = entryTiming.AdverseMoveBeforeRunPct
            };
        }

        private RecentFeatureSeries? TryBuildRecentSeries(
            CandidateEvaluationResult evaluation,
            BuildEvaluationDatasetSettings settings)
        {
            if (settings.SkipSeriesRebuildWhenPresent &&
                HasEvaluationSeries(evaluation))
            {
                return null;
            }

            if (!_historicalCache.TryLoad(evaluation.Ticker, Timeframe.M5, out var cached) ||
                cached == null ||
                cached.Count == 0)
            {
                return null;
            }

            var ordered = cached
                .Where(x => x.Time <= evaluation.ScanTime)
                .OrderBy(x => x.Time)
                .ToList();

            if (ordered.Count == 0)
                return null;

            var scanIndex = ordered.Count - 1;

            return new RecentFeatureSeries
            {
                DailyBbUpperBandSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyBollingerUpperBand),
                DailyBbMidBandSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyBollingerMidBand),
                DailyBbLowerBandSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyBollingerLowerBand),
                DailyRsiSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyRSI14),
                DailyMacdLineSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyMACDLine),
                DailyMacdSignalSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyMACDSignal),
                DailyMacdHistogramSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyMACDHistogram),
                WeeklyBbUpperBandSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyBollingerUpperBand ?? 0m),
                WeeklyBbMidBandSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyBollingerMidBand ?? 0m),
                WeeklyBbLowerBandSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyBollingerLowerBand ?? 0m),
                WeeklyRsiSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyRSI14 ?? 0m),
                WeeklyMacdLineSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyMACDLine ?? 0m),
                WeeklyMacdSignalSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyMACDSignal ?? 0m),
                WeeklyMacdHistogramSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyMACDHistogram ?? 0m),
                H4BbUpperBandSeries = BuildRecentH4Series(ordered, scanIndex, x => x.H4BollingerUpperBand),
                H4BbMidBandSeries = BuildRecentH4Series(ordered, scanIndex, x => x.H4BollingerMidBand),
                H4BbLowerBandSeries = BuildRecentH4Series(ordered, scanIndex, x => x.H4BollingerLowerBand),
                H4RsiSeries = BuildRecentH4Series(ordered, scanIndex, x => x.RSI14),
                H4MacdLineSeries = BuildRecentH4Series(ordered, scanIndex, x => x.MACDLine),
                H4MacdSignalSeries = BuildRecentH4Series(ordered, scanIndex, x => x.MACDSignal),
                H4MacdHistogramSeries = BuildRecentH4Series(ordered, scanIndex, x => x.MACDHistogram)
            };
        }

        private List<decimal> BuildRecentDailySeries(
            List<Candle> candles,
            int scanIndex,
            Func<FeatureSet, decimal> selector)
        {
            var indexes = new List<int>();
            var usedDays = new HashSet<DateTime>();

            for (var i = scanIndex; i >= 0; i--)
            {
                var day = candles[i].Time.Date;
                if (!usedDays.Add(day))
                    continue;

                indexes.Add(i);
                if (indexes.Count >= RecentDailySeriesLength)
                    break;
            }

            indexes.Reverse();
            return [.. indexes.Select(i => Round(selector(_featureEngine.Calculate(candles, i + 1))))];
        }

        private List<decimal> BuildRecentWeeklySeries(
            List<Candle> candles,
            int scanIndex,
            Func<FeatureSet, decimal> selector)
        {
            var indexes = new List<int>();
            var usedWeeks = new HashSet<DateTime>();

            for (var i = scanIndex; i >= 0; i--)
            {
                var candleTime = candles[i].Time;
                var week = candleTime.Date.AddDays(-(int)candleTime.DayOfWeek);
                if (!usedWeeks.Add(week))
                    continue;

                indexes.Add(i);
                if (indexes.Count >= RecentWeeklySeriesLength)
                    break;
            }

            indexes.Reverse();
            return [.. indexes.Select(i => Round(selector(_featureEngine.Calculate(candles, i + 1))))];
        }

        private List<decimal> BuildRecentH4Series(
            List<Candle> candles,
            int scanIndex,
            Func<FeatureSet, decimal> selector)
        {
            var indexes = new List<int>();
            var usedBuckets = new HashSet<DateTime>();

            for (var i = scanIndex; i >= 0; i--)
            {
                var bucket = StartOfH4Bucket(candles[i].Time);
                if (!usedBuckets.Add(bucket))
                    continue;

                indexes.Add(i);
                if (indexes.Count >= RecentH4SeriesLength)
                    break;
            }

            indexes.Reverse();
            return [.. indexes.Select(i => Round(selector(_featureEngine.Calculate(candles, i + 1))))];
        }

        private static string BuildEvaluationKey(CandidateEvaluationResult row)
        {
            return $"{row.Ticker}|{row.PresetScanCode}|{row.ScanTime:yyyy-MM-dd HH:mm:ss}";
        }

        private static List<decimal> ResolveSeries(
            List<decimal>? candidateSeries,
            List<decimal>? evaluationSeries,
            List<decimal>? fallbackSeries)
        {
            if (candidateSeries != null && candidateSeries.Count > 0)
                return [.. candidateSeries];

            if (evaluationSeries != null && evaluationSeries.Count > 0)
                return [.. evaluationSeries];

            if (fallbackSeries != null && fallbackSeries.Any(x => x != 0m))
                return [.. fallbackSeries];

            return [];
        }

        private static bool HasEvaluationSeries(CandidateEvaluationResult evaluation)
        {
            return (evaluation.RecentDailyBbUpperBandSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentDailyBbMidBandSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentDailyBbLowerBandSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentDailyRsiSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentDailyMacdLineSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentDailyMacdSignalSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentDailyMacdHistogramSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentWeeklyBbUpperBandSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentWeeklyBbMidBandSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentWeeklyBbLowerBandSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentWeeklyRsiSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentWeeklyMacdLineSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentWeeklyMacdSignalSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentWeeklyMacdHistogramSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentH4BbUpperBandSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentH4BbMidBandSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentH4BbLowerBandSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentH4RsiSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentH4MacdLineSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentH4MacdSignalSeries?.Count ?? 0) > 0 ||
                   (evaluation.RecentH4MacdHistogramSeries?.Count ?? 0) > 0;
        }

        private static bool HasEvaluationCacheMetrics(CandidateEvaluationResult evaluation)
        {
            return evaluation.MaxPct.HasValue &&
                   evaluation.MaxPrice.HasValue &&
                   evaluation.MaxTime.HasValue &&
                   evaluation.MinPct.HasValue &&
                   evaluation.MinPrice.HasValue &&
                   evaluation.MinTime.HasValue &&
                   evaluation.BestEntryDelayBarsM15.HasValue &&
                   evaluation.BestEntryDelayBarsH1.HasValue &&
                   evaluation.MinBeforeMaxPct.HasValue &&
                   evaluation.BarsToMin.HasValue &&
                   evaluation.BarsToMax.HasValue &&
                   evaluation.ReachedTargetBeforeEntry.HasValue &&
                   evaluation.OptimalEntryDiscountPct.HasValue &&
                   evaluation.AdverseMoveBeforeRunPct.HasValue;
        }

        private static string BuildCandidateKey(CandidateDetails row)
        {
            return $"{row.Ticker}|{row.Scan.PresetScanCode}|{row.Scan.ScanTime:yyyy-MM-dd HH:mm:ss}";
        }

        private static bool ShouldRebuildRow(
            CandidateEvaluationResult evaluation,
            Dictionary<string, RankedCandidateSnapshot> candidateIndex,
            Dictionary<string, EvaluationDatasetRow> existingRowIndex)
        {
            var key = BuildEvaluationKey(evaluation);
            if (!existingRowIndex.TryGetValue(key, out var existing))
                return true;

            if (existing.EvaluatedAt != evaluation.EvaluatedAt)
                return true;

            if (NeedsPostScanMetricsRebuild(existing))
                return true;

            return candidateIndex.ContainsKey(key);
        }

        private static bool NeedsPostScanMetricsRebuild(EvaluationDatasetRow row)
        {
            if (row.Outcome.Equals("InsufficientFutureData", StringComparison.OrdinalIgnoreCase))
                return false;

            return row.ScanPrice <= 0m ||
                   !row.ScanMovePct.HasValue ||
                   !row.CurrentPct.HasValue ||
                   !row.MaxPct.HasValue ||
                   !row.MaxPrice.HasValue ||
                   !row.MaxTime.HasValue ||
                   !row.MinPct.HasValue ||
                   !row.MinPrice.HasValue ||
                   !row.MinTime.HasValue ||
                   !row.PostMaxDrawdownPct.HasValue ||
                   !row.MinutesFromMinToMax.HasValue ||
                   !row.BestEntryDelayBarsM15.HasValue ||
                   !row.BestEntryDelayBarsH1.HasValue ||
                   !row.MinBeforeMaxPct.HasValue ||
                   !row.BarsToMin.HasValue ||
                   !row.BarsToMax.HasValue ||
                   !row.ReachedTargetBeforeEntry.HasValue ||
                   !row.OptimalEntryDiscountPct.HasValue ||
                   !row.AdverseMoveBeforeRunPct.HasValue ||
                   !row.MaxDownBeforeMaxUp.HasValue;
        }

        private async Task<List<EvaluationDatasetRow>> BuildLegacyNoEntryZeroAmplitudeBackfillRowsAsync(
            BuildEvaluationDatasetSettings settings,
            List<EvaluationDatasetRow> existingRows,
            Dictionary<string, RankedCandidateSnapshot> candidateIndex)
        {
            var backfillKeys = BuildLegacyNoEntryZeroAmplitudeKeys(
                settings,
                existingRows.ToDictionary(BuildDatasetKey, x => x, StringComparer.OrdinalIgnoreCase));
            if (backfillKeys.Count == 0)
                return [];

            var evaluationsPath = _pathService.GetEvaluationsFile();
            var evaluationsArchivePath = _pathService.GetEvaluationsArchiveFile();

            var evaluations = await _evaluationCsvService.ReadAsync(evaluationsPath);
            var archivedEvaluations = await _evaluationCsvService.ReadAsync(evaluationsArchivePath);
            evaluations.AddRange(archivedEvaluations);

            return [.. evaluations
                .Where(x => backfillKeys.Contains(BuildEvaluationKey(x)))
                .GroupBy(BuildEvaluationKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderByDescending(r => r.EvaluatedAt)
                    .ThenByDescending(r => r.EvaluationEndTime ?? DateTime.MinValue)
                    .First())
                .Select(x => BuildRow(x, candidateIndex))];
        }

        private static HashSet<string> BuildLegacyNoEntryZeroAmplitudeKeys(
            BuildEvaluationDatasetSettings settings,
            Dictionary<string, EvaluationDatasetRow> existingRowIndex)
        {
            if (!settings.BackfillLegacyNoEntryZeroAmplitudeDays.HasValue ||
                settings.BackfillLegacyNoEntryZeroAmplitudeDays.Value <= 0)
            {
                return [];
            }

            var cutoff = MarketTime.Now().Date.AddDays(-Math.Max(1, settings.BackfillLegacyNoEntryZeroAmplitudeDays.Value));

            return [.. existingRowIndex.Values
                .Where(x =>
                    x.ScanTime >= cutoff &&
                    string.Equals(x.Outcome, "NoEntry", StringComparison.OrdinalIgnoreCase) &&
                    x.AmplitudePct == 0m)
                .Select(BuildDatasetKey)];
        }

        private static string BuildDatasetKey(EvaluationDatasetRow row)
        {
            return $"{row.Ticker}|{row.PresetScanCode}|{row.ScanTime:yyyy-MM-dd HH:mm:ss}";
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

        private static string NormalizeCandidateGroup(string? groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return string.Empty;

            if (groupName.Equals("Runaway", StringComparison.OrdinalIgnoreCase) ||
                groupName.Equals("RunawayCandidates", StringComparison.OrdinalIgnoreCase) ||
                groupName.Equals("TodayResearchLikeCandidates", StringComparison.OrdinalIgnoreCase))
            {
                return "Runaway";
            }

            if (groupName.Equals("Reversal", StringComparison.OrdinalIgnoreCase) ||
                groupName.Equals("ReversalCandidates", StringComparison.OrdinalIgnoreCase))
            {
                return "Reversal";
            }

            return groupName;
        }

        private static int CompareRows(
            EvaluationDatasetRow left,
            EvaluationDatasetRow right,
            BuildEvaluationDatasetSettings settings)
        {
            foreach (var sortColumn in settings.SortColumns ?? [])
            {
                var comparison = CompareByColumn(left, right, sortColumn);
                if (comparison != 0)
                    return comparison;
            }

            return string.Compare(left.Ticker, right.Ticker, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareByColumn(
            EvaluationDatasetRow left,
            EvaluationDatasetRow right,
            BuildEvaluationDatasetSortColumnSettings sortColumn)
        {
            if (string.IsNullOrWhiteSpace(sortColumn.Column))
                return 0;

            var orderedValues = sortColumn.OrderedValues ?? [];
            var descending = sortColumn.Descending;
            var comparison = sortColumn.Column switch
            {
                nameof(EvaluationDatasetRow.GroupLabel) => CompareString(left.GroupLabel, right.GroupLabel, orderedValues),
                nameof(EvaluationDatasetRow.ScanTime) => left.ScanTime.CompareTo(right.ScanTime),
                nameof(EvaluationDatasetRow.CandidateGroup) => CompareString(left.CandidateGroup, right.CandidateGroup, orderedValues),
                nameof(EvaluationDatasetRow.CandidateDisplayRank) => CompareNullableInt(left.CandidateDisplayRank, right.CandidateDisplayRank),
                nameof(EvaluationDatasetRow.AmplitudePct) => left.AmplitudePct.CompareTo(right.AmplitudePct),
                nameof(EvaluationDatasetRow.Outcome) => CompareString(left.Outcome, right.Outcome, orderedValues),
                nameof(EvaluationDatasetRow.ExtremumOrder) => CompareString(left.ExtremumOrder, right.ExtremumOrder, orderedValues),
                nameof(EvaluationDatasetRow.ExtremumSubgroup) => CompareString(left.ExtremumSubgroup, right.ExtremumSubgroup, orderedValues),
                nameof(EvaluationDatasetRow.Ticker) => CompareString(left.Ticker, right.Ticker, orderedValues),
                _ => 0,
            };
            if (comparison == 0)
                return 0;

            return descending ? -comparison : comparison;
        }

        private static List<RankedCandidateSnapshot> BuildRankedCandidateSnapshots(
            string groupName,
            IEnumerable<CandidateDetails> candidates)
        {
            return [.. candidates
                .GroupBy(x => x.Scan.ScanTime)
                .SelectMany(scanGroup => OrderForDisplay(scanGroup)
                    .Select((candidate, index) => new RankedCandidateSnapshot(
                        candidate,
                        groupName,
                        (groupName.Equals("Runaway", StringComparison.OrdinalIgnoreCase) ||
                         groupName.Equals("RunawayCandidates", StringComparison.OrdinalIgnoreCase) ||
                         groupName.Equals("TodayResearchLikeCandidates", StringComparison.OrdinalIgnoreCase)) ? 0 : 1,
                        index + 1)))];
        }

        private static List<CandidateDetails> OrderForDisplay(IEnumerable<CandidateDetails> candidates)
        {
            return candidates
                .OrderByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
                .ThenByDescending(x => x.TradePlan.ProfitPercent)
                .ThenByDescending(x => x.Score.Score)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int CompareNullableInt(int? left, int? right)
        {
            var leftValue = left ?? int.MaxValue;
            var rightValue = right ?? int.MaxValue;
            return leftValue.CompareTo(rightValue);
        }

        private static int CompareString(
            string? left,
            string? right,
            List<string> orderedValues)
        {
            var leftValue = left ?? string.Empty;
            var rightValue = right ?? string.Empty;

            if (orderedValues.Count > 0)
            {
                var leftIndex = GetOrderedValueIndex(leftValue, orderedValues);
                var rightIndex = GetOrderedValueIndex(rightValue, orderedValues);
                var orderedComparison = leftIndex.CompareTo(rightIndex);
                if (orderedComparison != 0)
                    return orderedComparison;
            }

            return string.Compare(leftValue, rightValue, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetOrderedValueIndex(string value, List<string> orderedValues)
        {
            for (var i = 0; i < orderedValues.Count; i++)
            {
                if (string.Equals(orderedValues[i], value, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return int.MaxValue;
        }

        private static decimal CalcPctOrZero(decimal from, decimal to)
        {
            return from <= 0m
                ? 0m
                : ((to - from) / from) * 100m;
        }

        private static decimal CalcPct(decimal from, decimal to)
        {
            if (from <= 0m)
                return 0m;

            return ((to - from) / from) * 100m;
        }

        private static decimal CalculateAmplitudePct(
            decimal scanPrice,
            decimal? maxPct,
            DateTime? maxTime,
            decimal? minPct,
            DateTime? minTime)
        {
            if (scanPrice <= 0m)
                return 0m;

            var highPrice = maxPct.HasValue
                ? scanPrice * (1m + maxPct.Value / 100m)
                : scanPrice;
            var lowPrice = minPct.HasValue
                ? scanPrice * (1m + minPct.Value / 100m)
                : scanPrice;

            if (maxTime.HasValue && minTime.HasValue && minTime.Value <= maxTime.Value)
                return Round(CalcPct(lowPrice, highPrice));

            return Round(Math.Abs(CalcPct(lowPrice, highPrice)));
        }

        private static string GetMinDepthGroup(decimal? minPct)
        {
            var value = minPct ?? 0m;
            if (value <= -20m) return "Deep";
            if (value <= -10m) return "Medium";
            if (value < 0m) return "Shallow";
            return "None";
        }

        private static string GetMaxStrengthGroup(decimal? maxPct)
        {
            var value = maxPct ?? 0m;
            if (value >= 50m) return "Explosive";
            if (value >= 20m) return "Strong";
            if (value > 0m) return "Weak";
            return "None";
        }

        private static string BuildExtremumSubgroup(
            string extremumOrder,
            string minDepthGroup,
            string maxStrengthGroup)
        {
            return $"{extremumOrder}_{minDepthGroup}_{maxStrengthGroup}";
        }

        private static int? DiffMinutes(DateTime? from, DateTime? to)
        {
            if (!from.HasValue || !to.HasValue)
                return null;

            return (int)Math.Round((to.Value - from.Value).TotalMinutes, MidpointRounding.AwayFromZero);
        }

        private static decimal? CalculatePostMaxDrawdownPct(
            Candle maxAfterEntry,
            List<Candle> afterEntry)
        {
            var afterMax = afterEntry
                .Where(x => x.Time >= maxAfterEntry.Time)
                .ToList();

            if (afterMax.Count == 0 || maxAfterEntry.High <= 0m)
                return null;

            var minAfterMax = afterMax.Min(x => x.Low);
            return Round(CalcPct(maxAfterEntry.High, minAfterMax));
        }

        private static EntryTimingMetrics CalculateEntryTimingMetrics(
            List<Candle> ordered,
            decimal scanPrice,
            decimal entryPrice,
            decimal exitPrice,
            bool hasEntry)
        {
            if (ordered.Count == 0 || scanPrice <= 0m)
                return new EntryTimingMetrics();

            var maxIndex = IndexOfMaxHigh(ordered);
            var minIndex = IndexOfMinLow(ordered);
            var minBeforeMaxIndex = IndexOfMinLow(ordered.Take(maxIndex + 1).ToList());
            var minBeforeMaxPct = Round(CalcPct(scanPrice, ordered[minBeforeMaxIndex].Low));
            var optimalDiscountPct = Math.Max(-minBeforeMaxPct, 0m);
            var firstTargetIndex = exitPrice > 0m
                ? ordered.FindIndex(x => x.High >= exitPrice)
                : -1;
            var firstEntryIndex = entryPrice > 0m
                ? ordered.FindIndex(x => x.Low <= entryPrice && x.High >= entryPrice)
                : -1;
            var reachedTargetBeforeEntry = firstTargetIndex >= 0 &&
                                           (firstEntryIndex < 0 || firstTargetIndex < firstEntryIndex);

            return new EntryTimingMetrics
            {
                BestEntryDelayBarsM15 = NormalizeM5BarsToM15(minBeforeMaxIndex + 1),
                BestEntryDelayBarsH1 = NormalizeM5BarsToH1(minBeforeMaxIndex + 1),
                MinBeforeMaxPct = minBeforeMaxPct,
                BarsToMin = NormalizeM5BarsToM15(minIndex + 1),
                BarsToMax = NormalizeM5BarsToM15(maxIndex + 1),
                ReachedTargetBeforeEntry = reachedTargetBeforeEntry,
                EntryMissReason = hasEntry
                    ? string.Empty
                    : ResolveNoEntryMissReason(reachedTargetBeforeEntry, ordered, entryPrice),
                OptimalEntryDiscountPct = Round(optimalDiscountPct),
                AdverseMoveBeforeRunPct = Round(optimalDiscountPct)
            };
        }

        private static int IndexOfMaxHigh(List<Candle> candles)
        {
            var bestIndex = 0;
            for (var i = 1; i < candles.Count; i++)
            {
                if (candles[i].High > candles[bestIndex].High)
                    bestIndex = i;
            }

            return bestIndex;
        }

        private static int IndexOfMinLow(List<Candle> candles)
        {
            var bestIndex = 0;
            for (var i = 1; i < candles.Count; i++)
            {
                if (candles[i].Low < candles[bestIndex].Low)
                    bestIndex = i;
            }

            return bestIndex;
        }

        private static int NormalizeM5BarsToM15(int bars)
        {
            return Math.Max(1, (int)Math.Ceiling(bars / 3m));
        }

        private static int NormalizeM5BarsToH1(int bars)
        {
            return Math.Max(1, (int)Math.Ceiling(bars / 12m));
        }

        private static string ResolveNoEntryMissReason(
            bool reachedTargetBeforeEntry,
            List<Candle> ordered,
            decimal entryPrice)
        {
            if (reachedTargetBeforeEntry)
                return "TargetBeforeEntry";

            if (entryPrice <= 0m || ordered.Count == 0)
                return "NoEntry";

            var minLow = ordered.Min(x => x.Low);
            var maxHigh = ordered.Max(x => x.High);

            if (minLow > entryPrice)
                return "EntryTooDeep";

            if (maxHigh < entryPrice)
                return "GappedBelowEntry";

            return "NoEntry";
        }

        private static (decimal? ExitMissAbs, decimal? ExitMissPct, bool NearTakeProfitMiss) CalculateExitMiss(
            CandidateEvaluationResult evaluation,
            decimal maxHigh)
        {
            if (evaluation.ExitPrice <= 0m || maxHigh <= 0m)
                return (null, null, false);

            var missAbs = Math.Max(evaluation.ExitPrice - maxHigh, 0m);
            var missPct = evaluation.ExitPrice > 0m
                ? (decimal?)Round((missAbs / evaluation.ExitPrice) * 100m)
                : null;
            var nearMiss = missAbs > 0m &&
                           evaluation.ExitPrice > 0m &&
                           ((missAbs / evaluation.ExitPrice) * 100m) <= 1m;

            return (Round(missAbs), missPct, nearMiss);
        }

        private static string GetExtremumOrder(DateTime? minTime, DateTime? maxTime)
        {
            if (!minTime.HasValue || !maxTime.HasValue)
                return string.Empty;

            if (minTime.Value == maxTime.Value)
                return "SameBar";

            return minTime.Value < maxTime.Value ? "MinFirst" : "MaxFirst";
        }

        private static DateTime StartOfH4Bucket(DateTime time)
        {
            var hour = time.Hour - (time.Hour % 4);
            return new DateTime(time.Year, time.Month, time.Day, hour, 0, 0, time.Kind);
        }

        private static decimal Round(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private sealed class CacheMetrics
        {
            public decimal? MaxPct { get; init; }
            public decimal? MaxPrice { get; init; }
            public DateTime? MaxTime { get; init; }
            public decimal? MinPct { get; init; }
            public decimal? MinPrice { get; init; }
            public DateTime? MinTime { get; init; }
            public decimal? MaxPctBeforeEntry { get; init; }
            public decimal? MaxPriceBeforeEntry { get; init; }
            public DateTime? MaxTimeBeforeEntry { get; init; }
            public decimal? MinPctBeforeEntry { get; init; }
            public decimal? MinPriceBeforeEntry { get; init; }
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
            public int? BestEntryDelayBarsM15 { get; init; }
            public int? BestEntryDelayBarsH1 { get; init; }
            public decimal? MinBeforeMaxPct { get; init; }
            public int? BarsToMin { get; init; }
            public int? BarsToMax { get; init; }
            public bool? ReachedTargetBeforeEntry { get; init; }
            public string EntryMissReason { get; init; } = string.Empty;
            public decimal? OptimalEntryDiscountPct { get; init; }
            public decimal? AdverseMoveBeforeRunPct { get; init; }
        }

        private sealed class EntryTimingMetrics
        {
            public int? BestEntryDelayBarsM15 { get; init; }
            public int? BestEntryDelayBarsH1 { get; init; }
            public decimal? MinBeforeMaxPct { get; init; }
            public int? BarsToMin { get; init; }
            public int? BarsToMax { get; init; }
            public bool? ReachedTargetBeforeEntry { get; init; }
            public string EntryMissReason { get; init; } = string.Empty;
            public decimal? OptimalEntryDiscountPct { get; init; }
            public decimal? AdverseMoveBeforeRunPct { get; init; }
        }

        private sealed class RecentFeatureSeries
        {
            public List<decimal> DailyBbUpperBandSeries { get; init; } = [];
            public List<decimal> DailyBbMidBandSeries { get; init; } = [];
            public List<decimal> DailyBbLowerBandSeries { get; init; } = [];
            public List<decimal> DailyRsiSeries { get; init; } = [];
            public List<decimal> DailyMacdLineSeries { get; init; } = [];
            public List<decimal> DailyMacdSignalSeries { get; init; } = [];
            public List<decimal> DailyMacdHistogramSeries { get; init; } = [];
            public List<decimal> WeeklyBbUpperBandSeries { get; init; } = [];
            public List<decimal> WeeklyBbMidBandSeries { get; init; } = [];
            public List<decimal> WeeklyBbLowerBandSeries { get; init; } = [];
            public List<decimal> WeeklyRsiSeries { get; init; } = [];
            public List<decimal> WeeklyMacdLineSeries { get; init; } = [];
            public List<decimal> WeeklyMacdSignalSeries { get; init; } = [];
            public List<decimal> WeeklyMacdHistogramSeries { get; init; } = [];
            public List<decimal> H4BbUpperBandSeries { get; init; } = [];
            public List<decimal> H4BbMidBandSeries { get; init; } = [];
            public List<decimal> H4BbLowerBandSeries { get; init; } = [];
            public List<decimal> H4RsiSeries { get; init; } = [];
            public List<decimal> H4MacdLineSeries { get; init; } = [];
            public List<decimal> H4MacdSignalSeries { get; init; } = [];
            public List<decimal> H4MacdHistogramSeries { get; init; } = [];
        }

        private sealed record RankedCandidateSnapshot(
            CandidateDetails Candidate,
            string GroupName,
            int GroupPriority,
            int DisplayRank);
    }
}
