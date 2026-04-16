using IbSwingTrader.Domain.Dataset;

namespace IbSwingTrader.App.Commands
{
    public class BuildEvaluationDatasetCommand(
        ICandidateEvaluationCsvService evaluationCsvService,
        IJsonFileService jsonFileService,
        ICsvWriter csvWriter,
        IAgentPathService pathService,
        ITextLogger logger) : ICommand
    {
        private const decimal MinInterestingAmplitudePct = 5m;
        private const int MaxTradeDaysToMaxUpFromScan = 1;

        private readonly ICandidateEvaluationCsvService _evaluationCsvService = evaluationCsvService;
        private readonly IJsonFileService _jsonFileService = jsonFileService;
        private readonly ICsvWriter _csvWriter = csvWriter;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITextLogger _logger = logger;

        public async Task RunAsync()
        {
            var evaluationsPath = _pathService.GetEvaluationsFile();
            var outputPath = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), "datasets", "evaluation-dataset.csv"));

            var evaluations = await _evaluationCsvService.ReadAsync(evaluationsPath);

            if (evaluations.Count == 0)
            {
                _logger.Error("No evaluations found.");
                return;
            }

            var activeCandidates = await LoadCurrentCandidatesAsync();
            var candidateIndex = activeCandidates
                .GroupBy(BuildCandidateKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x
                        .OrderByDescending(c => c.Score.Score)
                        .First(),
                    StringComparer.OrdinalIgnoreCase);

            var rows = evaluations
                .Select(x => BuildRow(x, candidateIndex))
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

        private static EvaluationDatasetRow BuildRow(
            CandidateEvaluationResult evaluation,
            IReadOnlyDictionary<string, CandidateDetails> candidateIndex)
        {
            var key = BuildEvaluationKey(evaluation);
            candidateIndex.TryGetValue(key, out var candidate);
            var isFromWishlist = candidate?.IsFromWishlist ?? evaluation.IsFromWishlist;

            var positivePotentialPct = Round(Math.Max(evaluation.MaxPct ?? 0m, 0m));
            var negativePotentialPct = Round(Math.Abs(Math.Min(evaluation.MinPct ?? 0m, 0m)));
            var amplitudePct = Round(CalculateAmplitudePct(
                evaluation.ScanPrice,
                evaluation.EntryPrice,
                evaluation.MaxPct,
                evaluation.MaxUpTime,
                evaluation.MinPct,
                evaluation.MaxDownTime));

            var daysToMaxUpFromScan = DiffDays(evaluation.ScanTimeMarket, evaluation.MaxUpTime);
            var daysToMaxUpFromEntry = DiffDays(evaluation.EntryTime, evaluation.MaxUpTime);
            var daysToMaxDownFromScan = DiffDays(evaluation.ScanTimeMarket, evaluation.MaxDownTime);
            var daysToMaxDownFromEntry = DiffDays(evaluation.EntryTime, evaluation.MaxDownTime);

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
                MaxPct = evaluation.MaxPct,
                MaxUpTime = evaluation.MaxUpTime,
                MinPct = evaluation.MinPct,
                MaxDownTime = evaluation.MaxDownTime,
                PositivePotentialPct = positivePotentialPct,
                NegativePotentialPct = negativePotentialPct,
                AmplitudePct = amplitudePct,
                DaysToMaxUpFromScan = daysToMaxUpFromScan,
                DaysToMaxUpFromEntry = daysToMaxUpFromEntry,
                DaysToMaxDownFromScan = daysToMaxDownFromScan,
                DaysToMaxDownFromEntry = daysToMaxDownFromEntry,
                MaxDownBeforeMaxUp = CompareTimes(evaluation.MaxDownTime, evaluation.MaxUpTime),
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
    }
}
