namespace IbSwingTrader.App.Commands
{
    public class BuildEvaluationDatasetCommand(
        ICandidateEvaluationCsvService evaluationCsvService,
        IJsonFileService jsonFileService,
        IHistoricalCache historicalCache,
        IFeatureEngine featureEngine,
        ICsvWriter csvWriter,
        IAgentPathService pathService,
        IBuildEvaluationDatasetSettingsProvider buildEvaluationDatasetSettingsProvider,
        ITextLogger logger) : ICommand
    {
        private const decimal MinInterestingAmplitudePct = 5m;
        private const int MaxTradeDaysToMaxUpFromScan = 1;
        private const int ProgressLogInterval = 250;
        private const int RecentDailySeriesLength = 6;
        private const int RecentWeeklySeriesLength = 3;
        private const int RecentH4SeriesLength = 12;

        private readonly ICandidateEvaluationCsvService _evaluationCsvService = evaluationCsvService;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly IHistoricalCache _historicalCache = historicalCache;
        private readonly IFeatureEngine _featureEngine = featureEngine;
        private readonly ICsvWriter _csvWriter = csvWriter;
        private readonly IAgentPathService _pathService = pathService;
        private readonly IBuildEvaluationDatasetSettingsProvider _buildEvaluationDatasetSettingsProvider = buildEvaluationDatasetSettingsProvider;
        private readonly ITextLogger _logger = logger;

        public async Task RunAsync()
        {
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
            if (settings.MinScanTime.HasValue)
            {
                evaluations = evaluations
                    .Where(x => x.ScanTime >= settings.MinScanTime.Value)
                    .ToList();
            }

            if (evaluations.Count == 0)
            {
                _logger.Error("No evaluations found.");
                return;
            }

            _logger.Info($"Evaluations loaded: {evaluations.Count}");
            if (settings.MinScanTime.HasValue)
                _logger.Info($"MinScanTime filter: {settings.MinScanTime.Value:yyyy-MM-dd HH:mm:ss}");
            if (settings.MinAmplitudePct.HasValue)
                _logger.Info($"MinAmplitudePct filter: {settings.MinAmplitudePct.Value:0.##}");

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

            rows.Sort((left, right) => CompareRows(left, right, settings));

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
            if (document == null)
                return [];

            var primaryCandidates = document.Candidates
                .Select(x =>
                {
                    x.CandidateSource = string.IsNullOrWhiteSpace(x.CandidateSource) ? "Primary" : x.CandidateSource;
                    return x;
                });

            var sameDayCandidates = document.SameDayCandidates
                .Select(x =>
                {
                    x.CandidateSource = "SameDayContinuation";
                    return x;
                });

            return primaryCandidates
                .Concat(sameDayCandidates)
                .GroupBy(BuildCandidateKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }

        private EvaluationDatasetRow BuildRow(
            CandidateEvaluationResult evaluation,
            Dictionary<string, CandidateDetails> candidateIndex)
        {
            var key = BuildEvaluationKey(evaluation);
            candidateIndex.TryGetValue(key, out var candidate);
            var isFromWishlist = candidate?.IsFromWishlist ?? evaluation.IsFromWishlist;
            var candidateSource = candidate?.CandidateSource ?? evaluation.CandidateSource;
            if (string.IsNullOrWhiteSpace(candidateSource))
                candidateSource = "Primary";
            var cacheMetrics = TryBuildCacheMetrics(evaluation);
            var recentSeries = TryBuildRecentSeries(evaluation);

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

            return new EvaluationDatasetRow
            {
                Ticker = evaluation.Ticker,
                ScanTime = evaluation.ScanTime,
                EntryTime = evaluation.EntryTime,
                ExitTime = evaluation.ExitTime,
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
                MaxDownBeforeMaxUp = CompareTimes(minTime, maxTime),
                GroupLabel = Classify(amplitudePct, daysToMaxUpFromScan),
                CandidateSource = candidateSource,
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
                RecentDailyMaSeries = ResolveSeries(candidate?.RecentDailyMaSeries, evaluation.RecentDailyMaSeries, recentSeries?.DailyMaSeries),
                RecentDailyBbUpperDistanceSeries = ResolveSeries(candidate?.RecentDailyBbUpperDistanceSeries, evaluation.RecentDailyBbUpperDistanceSeries, recentSeries?.DailyBbUpperDistanceSeries),
                RecentDailyBbWidthSeries = ResolveSeries(candidate?.RecentDailyBbWidthSeries, evaluation.RecentDailyBbWidthSeries, recentSeries?.DailyBbWidthSeries),
                RecentDailyRsiSeries = ResolveSeries(candidate?.RecentDailyRsiSeries, evaluation.RecentDailyRsiSeries, recentSeries?.DailyRsiSeries),
                RecentDailyMacdSeries = ResolveSeries(candidate?.RecentDailyMacdSeries, evaluation.RecentDailyMacdSeries, recentSeries?.DailyMacdSeries),
                RecentWeeklyMaSeries = ResolveSeries(candidate?.RecentWeeklyMaSeries, evaluation.RecentWeeklyMaSeries, recentSeries?.WeeklyMaSeries),
                RecentWeeklyBbUpperDistanceSeries = ResolveSeries(candidate?.RecentWeeklyBbUpperDistanceSeries, evaluation.RecentWeeklyBbUpperDistanceSeries, recentSeries?.WeeklyBbUpperDistanceSeries),
                RecentWeeklyBbWidthSeries = ResolveSeries(candidate?.RecentWeeklyBbWidthSeries, evaluation.RecentWeeklyBbWidthSeries, recentSeries?.WeeklyBbWidthSeries),
                RecentWeeklyRsiSeries = ResolveSeries(candidate?.RecentWeeklyRsiSeries, evaluation.RecentWeeklyRsiSeries, recentSeries?.WeeklyRsiSeries),
                RecentWeeklyMacdSeries = ResolveSeries(candidate?.RecentWeeklyMacdSeries, evaluation.RecentWeeklyMacdSeries, recentSeries?.WeeklyMacdSeries),
                RecentH4MaSeries = ResolveSeries(candidate?.RecentH4MaSeries, evaluation.RecentH4MaSeries, recentSeries?.H4MaSeries),
                RecentH4BbUpperDistanceSeries = ResolveSeries(candidate?.RecentH4BbUpperDistanceSeries, evaluation.RecentH4BbUpperDistanceSeries, recentSeries?.H4BbUpperDistanceSeries),
                RecentH4BbWidthSeries = ResolveSeries(candidate?.RecentH4BbWidthSeries, evaluation.RecentH4BbWidthSeries, recentSeries?.H4BbWidthSeries),
                RecentH4RsiSeries = ResolveSeries(candidate?.RecentH4RsiSeries, evaluation.RecentH4RsiSeries, recentSeries?.H4RsiSeries),
                RecentH4MacdSeries = ResolveSeries(candidate?.RecentH4MacdSeries, evaluation.RecentH4MacdSeries, recentSeries?.H4MacdSeries)
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

            var evaluationEnd = evaluation.EvaluationEndTime ?? evaluation.EvaluatedAt;
            if (evaluationEnd <= evaluation.ScanTime)
                return null;

            var ordered = cached
                .Where(x => x.Time >= evaluation.ScanTime && x.Time <= evaluationEnd)
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
            decimal? maxPriceBeforeEntry = null;
            DateTime? maxTimeBeforeEntry = null;
            decimal? minPctBeforeEntry = null;
            decimal? minPriceBeforeEntry = null;
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
                ? Round(CalcPct(evaluation.ScanPrice, maxAfterEntry.High))
                : (decimal?)null;
            var maxPrice = Round(maxAfterEntry.High);

            var minPct = evaluation.ScanPrice > 0m
                ? Round(CalcPct(evaluation.ScanPrice, minAfterEntry.Low))
                : (decimal?)null;
            var minPrice = Round(minAfterEntry.Low);

            var postMaxDrawdownPct = CalculatePostMaxDrawdownPct(maxAfterEntry, afterEntry);
            var (exitMissAbs, exitMissPct, nearTakeProfitMiss) = CalculateExitMiss(evaluation, maxAfterEntry.High);

            return new CacheMetrics
            {
                MaxPct = maxPct,
                MaxPrice = maxPrice,
                MaxTime = maxAfterEntry.Time,
                MinPct = minPct,
                MinPrice = minPrice,
                MinTime = minAfterEntry.Time,
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
                ExtremumOrder = GetExtremumOrder(minAfterEntry.Time, maxAfterEntry.Time),
                MinutesFromMinToMax = DiffMinutes(minAfterEntry.Time, maxAfterEntry.Time),
                MinutesFromEntryToMax = DiffMinutes(evaluation.EntryTime, maxAfterEntry.Time),
                MinutesFromEntryToMin = DiffMinutes(evaluation.EntryTime, minAfterEntry.Time)
            };
        }

        private RecentFeatureSeries? TryBuildRecentSeries(CandidateEvaluationResult evaluation)
        {
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
                DailyMaSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyMaSignedDistancePct),
                DailyBbUpperDistanceSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyBollingerUpperDistancePct),
                DailyBbWidthSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyBollingerBandWidthPct),
                DailyRsiSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyRSI14),
                DailyMacdSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyMACDLineMinusSignal),
                WeeklyMaSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyMaSignedDistancePct),
                WeeklyBbUpperDistanceSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyBollingerUpperDistancePct),
                WeeklyBbWidthSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyBollingerBandWidthPct),
                WeeklyRsiSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyRSI14),
                WeeklyMacdSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyMACDLineMinusSignal),
                H4MaSeries = BuildRecentH4Series(ordered, scanIndex, x => x.H4MaSignedDistancePct),
                H4BbUpperDistanceSeries = BuildRecentH4Series(ordered, scanIndex, x => x.H4BollingerUpperDistancePct),
                H4BbWidthSeries = BuildRecentH4Series(ordered, scanIndex, x => x.H4BollingerBandWidthPct),
                H4RsiSeries = BuildRecentH4Series(ordered, scanIndex, x => x.RSI14),
                H4MacdSeries = BuildRecentH4Series(ordered, scanIndex, x => x.MACDLineMinusSignal)
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
            Func<FeatureSet, decimal?> selector)
        {
            var indexes = new List<int>();
            var usedWeeks = new HashSet<DateTime>();

            for (var i = scanIndex; i >= 0; i--)
            {
                var week = StartOfWeek(candles[i].Time);
                if (!usedWeeks.Add(week))
                    continue;

                indexes.Add(i);
                if (indexes.Count >= RecentWeeklySeriesLength)
                    break;
            }

            indexes.Reverse();

            return [.. indexes
                .Select(i => selector(_featureEngine.Calculate(candles, i + 1)))
                .Where(x => x.HasValue)
                .Select(x => Round(x!.Value))];
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

        private static string BuildCandidateKey(CandidateDetails row)
        {
            return $"{row.Ticker}|{row.Scan.PresetScanCode}|{row.Scan.ScanTime:yyyy-MM-dd HH:mm:ss}";
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
                nameof(EvaluationDatasetRow.ScanTime) => left.ScanTime.Date.CompareTo(right.ScanTime.Date),
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
            decimal? maxUpPct,
            DateTime? maxUpTime,
            decimal? maxDownPct,
            DateTime? maxDownTime)
        {
            var positivePotentialPct = Math.Max(maxUpPct ?? 0m, 0m);
            var negativePotentialPct = Math.Abs(Math.Min(maxDownPct ?? scanPrice, scanPrice));

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

        private static string GetMinDepthGroup(decimal? minPct)
        {
            if (!minPct.HasValue)
                return "Unknown";

            if (minPct.Value >= 0m)
                return "NoDip";

            var drawdownPct = Math.Abs(minPct.Value);

            if (drawdownPct < 1m)
                return "MicroDip";

            if (drawdownPct < 2m)
                return "Shallow";

            if (drawdownPct < 5m)
                return "Medium";

            return "Deep";
        }

        private static string GetMaxStrengthGroup(decimal? maxPct)
        {
            if (!maxPct.HasValue)
                return "Unknown";

            var upsidePct = Math.Max(maxPct.Value, 0m);

            if (upsidePct < 5m)
                return "Weak";

            if (upsidePct < 10m)
                return "Strong";

            if (upsidePct < 20m)
                return "Explosive";

            return "Parabolic";
        }

        private static string BuildExtremumSubgroup(
            string extremumOrder,
            string minDepthGroup,
            string maxStrengthGroup)
        {
            if (string.IsNullOrWhiteSpace(extremumOrder))
                return "Unknown";

            return $"{extremumOrder}_{minDepthGroup}_{maxStrengthGroup}";
        }

        private static DateTime StartOfWeek(DateTime time)
        {
            var date = time.Date;
            var diff = ((int)date.DayOfWeek + 6) % 7;
            return date.AddDays(-diff);
        }

        private static DateTime StartOfH4Bucket(DateTime time)
        {
            return new DateTime(
                time.Year,
                time.Month,
                time.Day,
                (time.Hour / 4) * 4,
                0,
                0,
                time.Kind);
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

        private sealed class RecentFeatureSeries
        {
            public List<decimal> DailyMaSeries { get; init; } = [];
            public List<decimal> DailyBbUpperDistanceSeries { get; init; } = [];
            public List<decimal> DailyBbWidthSeries { get; init; } = [];
            public List<decimal> DailyRsiSeries { get; init; } = [];
            public List<decimal> DailyMacdSeries { get; init; } = [];
            public List<decimal> WeeklyMaSeries { get; init; } = [];
            public List<decimal> WeeklyBbUpperDistanceSeries { get; init; } = [];
            public List<decimal> WeeklyBbWidthSeries { get; init; } = [];
            public List<decimal> WeeklyRsiSeries { get; init; } = [];
            public List<decimal> WeeklyMacdSeries { get; init; } = [];
            public List<decimal> H4MaSeries { get; init; } = [];
            public List<decimal> H4BbUpperDistanceSeries { get; init; } = [];
            public List<decimal> H4BbWidthSeries { get; init; } = [];
            public List<decimal> H4RsiSeries { get; init; } = [];
            public List<decimal> H4MacdSeries { get; init; } = [];
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
        }
    }
}
