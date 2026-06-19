using IbSwingTrader.Common.Time;

namespace IbSwingTrader.App.Commands
{
    public class EvaluateCandidatesCommand(
        ITwsConnection twsConnection,
        ICandidateEvaluator candidateEvaluator,
        IEvaluationDatasetBuilder evaluationDatasetBuilder,
        ICandidateFileService candidateFileService,
        ICandidateEvaluationSettingsProvider candidateEvaluationSettingsProvider,
        ITextLogger logger,
        IAgentPathService pathService,
        ITwsSettingsProvider twsSettingsProvider) : ICommand
    {
        private readonly ITwsConnection _twsConnection = twsConnection;
        private readonly ICandidateEvaluator _candidateEvaluator = candidateEvaluator;
        private readonly IEvaluationDatasetBuilder _evaluationDatasetBuilder = evaluationDatasetBuilder;
        private readonly ICandidateFileService _candidateFileService = candidateFileService;
        private readonly ICandidateEvaluationSettingsProvider _candidateEvaluationSettingsProvider = candidateEvaluationSettingsProvider;
        private readonly ITextLogger _logger = logger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITwsSettingsProvider _twsSettingsProvider = twsSettingsProvider;

        public async Task RunAsync()
        {
            var twsSettings = _twsSettingsProvider.Get();

            var candidatesPath = _pathService.GetCandidatesFile();

            EnsureConnected(twsSettings);

            await EvaluateCandidatesAsync(candidatesPath);

            _logger.Info("Candidate evaluation completed.");
        }

        private async Task EvaluateCandidatesAsync(
            string candidatesPath)
        {
            var candidates = await LoadCandidatesAsync(candidatesPath);
            _logger.Info($"Candidates found: {candidates.Count}");
            var evaluationSettings = _candidateEvaluationSettingsProvider.Get();
            var existingDatasetRows = await _evaluationDatasetBuilder.ReadCurrentAsync();

            var marketToday = MarketTime.Now().Date;
            var availableNow = MarketTime.Now().AddMinutes(-Math.Max(0, evaluationSettings.FreshDataSafetyLagMinutes));
            var evaluableScanDates = candidates
                .Where(x => x.Scan.ScanTime < availableNow)
                .Select(x => x.Scan.ScanTime.Date)
                .Distinct()
                .OrderByDescending(x => x)
                .ToList();
            var latestEvaluatedScanDate = existingDatasetRows.Count == 0
                ? DateTime.MinValue
                : existingDatasetRows.Max(x => x.ScanTime.Date);
            var recentScanDateCount = Math.Max(1, evaluationSettings.ForwardEvaluationDays + 1);
            var recentScanDates = evaluableScanDates
                .Take(recentScanDateCount)
                .ToHashSet();
            var selectedScanDates = evaluableScanDates
                .Where(x => recentScanDates.Contains(x) || x > latestEvaluatedScanDate)
                .OrderBy(x => x)
                .ToList();

            if (selectedScanDates.Count == 0)
            {
                _logger.Info(
                    $"Nothing to evaluate. Current market date={marketToday:yyyy-MM-dd}, " +
                    $"availableUntil={availableNow:yyyy-MM-dd HH:mm:ss}, " +
                    $"latestEvaluatedScanDate={FormatDate(latestEvaluatedScanDate)}, " +
                    $"candidates={candidates.Count}");
                return;
            }

            var selectedScanDateSet = selectedScanDates.ToHashSet();
            var selectedScanCandidates = candidates
                .Where(x => selectedScanDateSet.Contains(x.Scan.ScanTime.Date))
                .ToList();
            var newestSelectedScanDate = selectedScanDates.Max();
            var oldestSelectedScanDate = selectedScanDates.Min();
            var newerCandidates = candidates.Count(x => x.Scan.ScanTime.Date > newestSelectedScanDate);
            var olderCandidates = candidates.Count(x => x.Scan.ScanTime.Date < oldestSelectedScanDate);
            var betweenSkippedCandidates = candidates.Count -
                                           selectedScanCandidates.Count -
                                           newerCandidates -
                                           olderCandidates;

            _logger.Info(
                $"Evaluating available scan dates: {string.Join(", ", selectedScanDates.Select(x => x.ToString("yyyy-MM-dd")))}. " +
                $"Selected candidates={selectedScanCandidates.Count}, " +
                $"skipped older candidates={olderCandidates}, " +
                $"skipped between candidates={betweenSkippedCandidates}, " +
                $"skipped newer candidates={newerCandidates}, " +
                $"latestEvaluatedScanDate={FormatDate(latestEvaluatedScanDate)}, " +
                $"availableUntil={availableNow:yyyy-MM-dd HH:mm:ss}");

            var canonicalByScanKey = existingDatasetRows
                .GroupBy(BuildScanKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x
                        .OrderByDescending(y => y.EvaluatedAt)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var candidatesToEvaluate = selectedScanCandidates
                .GroupBy(BuildScanKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            if (evaluationSettings.ReevaluateAllCandidatesWithSeries)
            {
                var candidatesWithSeries = selectedScanCandidates
                    .Where(HasRecentSeries)
                    .GroupBy(BuildScanKey, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .ToList();

                candidatesToEvaluate = candidatesWithSeries
                    .ToDictionary(BuildScanKey, x => x, StringComparer.OrdinalIgnoreCase);

                _logger.Info(
                    $"Selected-date reevaluation with series enabled: selected={candidatesToEvaluate.Count}, " +
                    $"ignoredWithoutSeries={selectedScanCandidates.Count - candidatesToEvaluate.Count}, " +
                    $"skippedOtherSeriesCandidates={candidates.Count - selectedScanCandidates.Count}");
            }

            if (evaluationSettings.ReevaluateOpenCandidates)
            {
                var oldestSelectedDate = selectedScanDates.Min();
                var openCandidates = existingDatasetRows
                    .Where(x =>
                        string.Equals(x.Outcome, "Open", StringComparison.OrdinalIgnoreCase) &&
                        !x.IsStaleOpen &&
                        x.ScanTime.Date < oldestSelectedDate)
                    .GroupBy(BuildScanKey, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x
                        .OrderByDescending(y => y.EvaluatedAt)
                        .First())
                    .Select(RebuildCandidateFromDatasetRow)
                    .ToList();

                foreach (var openCandidate in openCandidates)
                    candidatesToEvaluate[BuildScanKey(openCandidate)] = openCandidate;

                _logger.Info($"Open reevaluation enabled: additionalOpenCandidates={openCandidates.Count}");
            }

            _logger.Info(
                $"Candidate evaluation selection: pending={candidatesToEvaluate.Count}, " +
                "already evaluated rows will be overwritten");

            if (candidatesToEvaluate.Count == 0)
            {
                _logger.Info(
                    $"Nothing to evaluate. Current market date={marketToday:yyyy-MM-dd}, " +
                    $"selected scan dates={string.Join(", ", selectedScanDates.Select(x => x.ToString("yyyy-MM-dd")))}, " +
                    $"candidates={candidates.Count}");
                return;
            }

            var results = await _candidateEvaluator.EvaluateAsync(candidatesToEvaluate.Values.ToList());
            _logger.Info($"Evaluation step: candidate evaluator returned {results.Count} results");

            foreach (var result in results)
            {
                if (canonicalByScanKey.TryGetValue(BuildScanKey(result), out var canonical))
                {
                    result.StrategyVersion = canonical.StrategyVersion;
                    result.CandidateScore = canonical.CandidateScore ?? result.CandidateScore;
                }
            }

            _logger.Info($"Evaluation step: merging {results.Count} rows directly into evaluation dataset");
            await _evaluationDatasetBuilder.UpsertAsync(results);
            _logger.Info("Evaluation step: evaluation dataset merge completed");
            LogCandidateSummary(Path.GetFileName(candidatesPath), results);
        }

        private void EnsureConnected(TwsSettings twsSettings)
        {
            if (_twsConnection.IsConnected)
                return;

            _logger.Info("Connecting to TWS...");

            _twsConnection.Connect(twsSettings.Host, twsSettings.Port, twsSettings.ClientId);

            var connected = _twsConnection.Ready.Task
                .Wait(TimeSpan.FromSeconds(twsSettings.ConnectTimeoutSeconds));

            if (!connected || !_twsConnection.IsConnected)
                throw new InvalidOperationException("Failed to connect to TWS.");

            _logger.Info("TWS connected.");
        }

        private static string FormatDate(DateTime value)
        {
            return value == DateTime.MinValue
                ? "none"
                : value.ToString("yyyy-MM-dd");
        }

        private void LogCandidateSummary(
            string sourceFileName,
            List<CandidateEvaluationResult> results)
        {
            var wins = results.Count(x => x.Outcome == "Win");
            var losses = results.Count(x => x.Outcome == "Loss");
            var open = results.Count(x => x.Outcome == "Open");
            var noEntry = results.Count(x => x.Outcome == "NoEntry");
            var noData = results.Count(x =>
                x.Outcome == "NoData" ||
                x.Outcome == "NoDataAfterScan");
            var errors = results.Count(x =>
                x.Outcome != null &&
                x.Outcome.StartsWith("Error:", StringComparison.OrdinalIgnoreCase));

            _logger.Info(
                $"Done {sourceFileName} | Total={results.Count} Win={wins} Loss={losses} Open={open} NoEntry={noEntry} NoData={noData} Errors={errors}");
        }

        private async Task<List<CandidateDetails>> LoadCandidatesAsync(string candidatesPath)
        {
            var document = await _candidateFileService.ReadAsync(candidatesPath);

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

            return sameDayCandidates
                .Concat(primaryCandidates)
                .GroupBy(BuildScanKey, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }

        private static string BuildScanKey(CandidateDetails candidate)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{candidate.Ticker}|{candidate.Scan.PresetScanCode}|{candidate.Scan.ScanTime:O}");
        }

        private static bool HasRecentSeries(CandidateDetails candidate)
        {
            return (candidate.RecentDailyBbUpperBandSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyBbMidBandSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyBbLowerBandSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyRsiSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyMacdLineSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyMacdSignalSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyMacdHistogramSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyBbUpperBandSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyBbMidBandSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyBbLowerBandSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyRsiSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyMacdLineSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyMacdSignalSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyMacdHistogramSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4BbUpperBandSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4BbMidBandSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4BbLowerBandSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4RsiSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4MacdLineSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4MacdSignalSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4MacdHistogramSeries?.Count ?? 0) > 0;
        }

        private static string BuildScanKey(CandidateEvaluationResult result)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{result.Ticker}|{result.PresetScanCode}|{result.ScanTime:O}");
        }

        private static string BuildScanKey(EvaluationDatasetRow result)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{result.Ticker}|{result.PresetScanCode}|{result.ScanTime:O}");
        }

        private static CandidateDetails RebuildCandidateFromDatasetRow(EvaluationDatasetRow evaluation)
        {
            return new CandidateDetails
            {
                Ticker = evaluation.Ticker,
                CandidateSource = string.IsNullOrWhiteSpace(evaluation.CandidateSource) ? "Primary" : evaluation.CandidateSource,
                RecentDailyBbUpperBandSeries = [.. evaluation.RecentDailyBbUpperBandSeries],
                RecentDailyBbMidBandSeries = [.. evaluation.RecentDailyBbMidBandSeries],
                RecentDailyBbLowerBandSeries = [.. evaluation.RecentDailyBbLowerBandSeries],
                RecentDailyRsiSeries = [.. evaluation.RecentDailyRsiSeries],
                RecentDailyMacdLineSeries = [.. evaluation.RecentDailyMacdLineSeries],
                RecentDailyMacdSignalSeries = [.. evaluation.RecentDailyMacdSignalSeries],
                RecentDailyMacdHistogramSeries = [.. evaluation.RecentDailyMacdHistogramSeries],
                RecentWeeklyBbUpperBandSeries = [.. evaluation.RecentWeeklyBbUpperBandSeries],
                RecentWeeklyBbMidBandSeries = [.. evaluation.RecentWeeklyBbMidBandSeries],
                RecentWeeklyBbLowerBandSeries = [.. evaluation.RecentWeeklyBbLowerBandSeries],
                RecentWeeklyRsiSeries = [.. evaluation.RecentWeeklyRsiSeries],
                RecentWeeklyMacdLineSeries = [.. evaluation.RecentWeeklyMacdLineSeries],
                RecentWeeklyMacdSignalSeries = [.. evaluation.RecentWeeklyMacdSignalSeries],
                RecentWeeklyMacdHistogramSeries = [.. evaluation.RecentWeeklyMacdHistogramSeries],
                RecentH4BbUpperBandSeries = [.. evaluation.RecentH4BbUpperBandSeries],
                RecentH4BbMidBandSeries = [.. evaluation.RecentH4BbMidBandSeries],
                RecentH4BbLowerBandSeries = [.. evaluation.RecentH4BbLowerBandSeries],
                RecentH4RsiSeries = [.. evaluation.RecentH4RsiSeries],
                RecentH4MacdLineSeries = [.. evaluation.RecentH4MacdLineSeries],
                RecentH4MacdSignalSeries = [.. evaluation.RecentH4MacdSignalSeries],
                RecentH4MacdHistogramSeries = [.. evaluation.RecentH4MacdHistogramSeries],
                WeeklyBbDirection = evaluation.WeeklyBbDirection,
                WeeklyBbRegime = evaluation.WeeklyBbRegime,
                DailyBbDirection = evaluation.DailyBbDirection,
                DailyBbRegime = evaluation.DailyBbRegime,
                H4BbDirection = evaluation.H4BbDirection,
                H4BbRegime = evaluation.H4BbRegime,
                IsFromWishlist = evaluation.IsFromWishlist,
                Scan = new ScanInfo
                {
                    PresetScanCode = evaluation.PresetScanCode,
                    ScanTime = evaluation.ScanTime
                },
                Score = new ScoreInfo
                {
                    Score = evaluation.CandidateScore ?? 0m
                },
                Context = new MarketContextInfo(),
                TradePlan = new TradePlanInfo
                {
                    EntryPrice = evaluation.EntryPrice,
                    ExitPrice = evaluation.ExitPrice,
                    StopLoss = evaluation.StopLoss,
                    StopLimitPrice = evaluation.StopLoss,
                    ProfitPercent = CalcPct(evaluation.EntryPrice, evaluation.ExitPrice),
                    LossPercent = CalcPct(evaluation.EntryPrice, evaluation.StopLoss)
                }
            };
        }

        private static decimal CalcPct(decimal from, decimal to)
        {
            if (from == 0m)
                return 0m;

            return (to - from) / from * 100m;
        }
    }
}
