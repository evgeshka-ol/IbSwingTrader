using IbSwingTrader.Common.Time;

namespace IbSwingTrader.App.Commands
{
    public class EvaluateCandidatesCommand(
        ITwsConnection twsConnection,
        ICandidateEvaluator candidateEvaluator,
        IEvaluationDatasetBuilder evaluationDatasetBuilder,
        IJsonFileService jsonFileService,
        ICandidateEvaluationCsvService candidateCsvService,
        ICandidateEvaluationSettingsProvider candidateEvaluationSettingsProvider,
        ITextLogger logger,
        IAgentPathService pathService,
        ITwsSettingsProvider twsSettingsProvider) : ICommand
    {
        private readonly ITwsConnection _twsConnection = twsConnection;
        private readonly ICandidateEvaluator _candidateEvaluator = candidateEvaluator;
        private readonly IEvaluationDatasetBuilder _evaluationDatasetBuilder = evaluationDatasetBuilder;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly ICandidateEvaluationCsvService _candidateCsvService = candidateCsvService;
        private readonly ICandidateEvaluationSettingsProvider _candidateEvaluationSettingsProvider = candidateEvaluationSettingsProvider;
        private readonly ITextLogger _logger = logger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITwsSettingsProvider _twsSettingsProvider = twsSettingsProvider;

        public async Task RunAsync()
        {
            var twsSettings = _twsSettingsProvider.Get();

            var candidatesPath = _pathService.GetCandidatesFile();
            var evaluationsPath = _pathService.GetEvaluationsFile();

            EnsureConnected(twsSettings.ConnectTimeoutSeconds);

            var evaluationsFolder = Path.GetDirectoryName(evaluationsPath);
            if (!string.IsNullOrWhiteSpace(evaluationsFolder))
                Directory.CreateDirectory(evaluationsFolder);

            await EvaluateCandidatesAsync(candidatesPath, evaluationsPath);

            _logger.Info("Rebuilding evaluation dataset after candidate evaluation...");
            await _evaluationDatasetBuilder.RunAsync();

            _logger.Info("Candidate evaluation completed.");
        }

        private async Task EvaluateCandidatesAsync(
            string candidatesPath,
            string evaluationsPath)
        {
            var candidates = await LoadCandidatesAsync(candidatesPath);
            _logger.Info($"Candidates found: {candidates.Count}");
            var evaluationSettings = _candidateEvaluationSettingsProvider.Get();

            var marketToday = MarketTime.Now().Date;
            var evaluationScanDate = marketToday.AddDays(-1);
            var latestScanCandidates = candidates
                .Where(x => x.Scan.ScanTime.Date == evaluationScanDate)
                .ToList();
            var currentDayCandidates = candidates.Count(x => x.Scan.ScanTime.Date >= marketToday);
            var olderCandidates = candidates.Count - latestScanCandidates.Count - currentDayCandidates;

            _logger.Info(
                $"Evaluating previous scan date only: {evaluationScanDate:yyyy-MM-dd}. " +
                $"Previous-day candidates={latestScanCandidates.Count}, " +
                $"skipped older candidates={olderCandidates}, " +
                $"skipped current-day candidates={currentDayCandidates}");

            if (!File.Exists(evaluationsPath))
                await _candidateCsvService.WriteAsync(evaluationsPath, []);

            var existingEvaluations = await _candidateCsvService.ReadAsync(evaluationsPath);
            var canonicalByScanKey = existingEvaluations
                .GroupBy(BuildScanKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x
                        .OrderByDescending(y => y.EvaluatedAt)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var candidatesToEvaluate = latestScanCandidates
                .GroupBy(BuildScanKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            if (evaluationSettings.ReevaluateAllCandidatesWithSeries)
            {
                var candidatesWithSeries = latestScanCandidates
                    .Where(HasRecentSeries)
                    .GroupBy(BuildScanKey, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .ToList();

                candidatesToEvaluate = candidatesWithSeries
                    .ToDictionary(BuildScanKey, x => x, StringComparer.OrdinalIgnoreCase);

                _logger.Info(
                    $"Previous-day reevaluation with series enabled: selected={candidatesToEvaluate.Count}, " +
                    $"ignoredWithoutSeries={latestScanCandidates.Count - candidatesToEvaluate.Count}, " +
                    $"skippedOlderSeriesCandidates={candidates.Count - latestScanCandidates.Count}");
            }

            if (evaluationSettings.ReevaluateOpenCandidates)
            {
                var openCandidates = existingEvaluations
                    .Where(x =>
                        string.Equals(x.Outcome, "Open", StringComparison.OrdinalIgnoreCase) &&
                        !x.IsStaleOpen &&
                        x.ScanTime.Date < evaluationScanDate)
                    .GroupBy(BuildScanKey, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x
                        .OrderByDescending(y => y.EvaluatedAt)
                        .First())
                    .Select(RebuildCandidateFromEvaluation)
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
                    $"expected scan date={evaluationScanDate:yyyy-MM-dd}, candidates={candidates.Count}");
                return;
            }

            var results = await _candidateEvaluator.EvaluateAsync(candidatesToEvaluate.Values.ToList());
            _logger.Info($"Evaluation step: candidate evaluator returned {results.Count} results");

            foreach (var result in results)
            {
                if (canonicalByScanKey.TryGetValue(BuildScanKey(result), out var canonical))
                {
                    result.StrategyVersion = canonical.StrategyVersion;
                    result.CandidateScore = canonical.CandidateScore;
                }
            }

            _logger.Info($"Evaluation step: writing {results.Count} evaluation rows to CSV");
            await _candidateCsvService.WriteAsync(evaluationsPath, results);
            _logger.Info("Evaluation step: evaluation CSV write completed");
            LogCandidateSummary(Path.GetFileName(candidatesPath), results);
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
            if (!File.Exists(candidatesPath))
                return [];

            var json = await File.ReadAllTextAsync(candidatesPath);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            var firstNonWhitespace = json.FirstOrDefault(x => !char.IsWhiteSpace(x));

            if (firstNonWhitespace == '[')
                return await _jsonFileService.ReadAsync<List<CandidateDetails>>(candidatesPath) ?? [];

            var document = await _jsonFileService.ReadAsync<CandidateFileDocument>(candidatesPath);
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
            return (candidate.RecentDailyMaSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyBbMidDistanceSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyBbUpperDistanceSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyBbWidthSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyRsiSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentDailyMacdSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyMaSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyBbMidDistanceSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyBbUpperDistanceSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyBbWidthSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyRsiSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentWeeklyMacdSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4MaSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4BbMidDistanceSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4BbUpperDistanceSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4BbWidthSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4RsiSeries?.Count ?? 0) > 0 ||
                   (candidate.RecentH4MacdSeries?.Count ?? 0) > 0;
        }

        private static string BuildScanKey(CandidateEvaluationResult result)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{result.Ticker}|{result.PresetScanCode}|{result.ScanTime:O}");
        }

        private static CandidateDetails RebuildCandidateFromEvaluation(CandidateEvaluationResult evaluation)
        {
            return new CandidateDetails
            {
                Ticker = evaluation.Ticker,
                CandidateSource = string.IsNullOrWhiteSpace(evaluation.CandidateSource) ? "Primary" : evaluation.CandidateSource,
                RecentDailyMaSeries = [.. evaluation.RecentDailyMaSeries],
                RecentDailyBbMidDistanceSeries = [.. evaluation.RecentDailyBbMidDistanceSeries],
                RecentDailyBbUpperDistanceSeries = [.. evaluation.RecentDailyBbUpperDistanceSeries],
                RecentDailyBbWidthSeries = [.. evaluation.RecentDailyBbWidthSeries],
                RecentDailyRsiSeries = [.. evaluation.RecentDailyRsiSeries],
                RecentDailyMacdSeries = [.. evaluation.RecentDailyMacdSeries],
                RecentWeeklyMaSeries = [.. evaluation.RecentWeeklyMaSeries],
                RecentWeeklyBbMidDistanceSeries = [.. evaluation.RecentWeeklyBbMidDistanceSeries],
                RecentWeeklyBbUpperDistanceSeries = [.. evaluation.RecentWeeklyBbUpperDistanceSeries],
                RecentWeeklyBbWidthSeries = [.. evaluation.RecentWeeklyBbWidthSeries],
                RecentWeeklyRsiSeries = [.. evaluation.RecentWeeklyRsiSeries],
                RecentWeeklyMacdSeries = [.. evaluation.RecentWeeklyMacdSeries],
                RecentH4MaSeries = [.. evaluation.RecentH4MaSeries],
                RecentH4BbMidDistanceSeries = [.. evaluation.RecentH4BbMidDistanceSeries],
                RecentH4BbUpperDistanceSeries = [.. evaluation.RecentH4BbUpperDistanceSeries],
                RecentH4BbWidthSeries = [.. evaluation.RecentH4BbWidthSeries],
                RecentH4RsiSeries = [.. evaluation.RecentH4RsiSeries],
                RecentH4MacdSeries = [.. evaluation.RecentH4MacdSeries],
                WeeklyBbDirection = evaluation.WeeklyBbDirection,
                WeeklyBbRegime = evaluation.WeeklyBbRegime,
                WeeklyBbMidSlope = evaluation.WeeklyBbMidSlope,
                WeeklyBbWidthSlope = evaluation.WeeklyBbWidthSlope,
                WeeklyBbUpperDistanceSlope = evaluation.WeeklyBbUpperDistanceSlope,
                DailyBbDirection = evaluation.DailyBbDirection,
                DailyBbRegime = evaluation.DailyBbRegime,
                DailyBbMidSlope = evaluation.DailyBbMidSlope,
                DailyBbWidthSlope = evaluation.DailyBbWidthSlope,
                DailyBbUpperDistanceSlope = evaluation.DailyBbUpperDistanceSlope,
                H4BbDirection = evaluation.H4BbDirection,
                H4BbRegime = evaluation.H4BbRegime,
                H4BbMidSlope = evaluation.H4BbMidSlope,
                H4BbWidthSlope = evaluation.H4BbWidthSlope,
                H4BbUpperDistanceSlope = evaluation.H4BbUpperDistanceSlope,
                IsFromWishlist = evaluation.IsFromWishlist,
                Scan = new ScanInfo
                {
                    PresetScanCode = evaluation.PresetScanCode,
                    ScanTime = evaluation.ScanTime
                },
                Score = new ScoreInfo
                {
                    Score = evaluation.CandidateScore
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
