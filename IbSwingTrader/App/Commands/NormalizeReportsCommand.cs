namespace IbSwingTrader.App.Commands
{
    public class NormalizeReportsCommand(
        INormalizeReportsSettingsProvider normalizeReportsSettingsProvider,
        ICandidateFileService candidateFileService,
        IWishListReader wishListReader,
        IWishListResultWriter wishListWriter,
        IHistoricalCache historicalCache,
        IFeatureEngine featureEngine,
        NormalizeEvaluationsCommand normalizeEvaluationsCommand,
        IEvaluationDatasetCsvService evaluationDatasetCsvService,
        IAgentPathService pathService,
        ITextLogger logger) : ICommand
    {
        private const int RecentDailySeriesLength = RecentSeriesWindow.Daily;
        private const int RecentWeeklySeriesLength = RecentSeriesWindow.Weekly;
        private const int RecentH4SeriesLength = RecentSeriesWindow.H4;

        private readonly INormalizeReportsSettingsProvider _normalizeReportsSettingsProvider = normalizeReportsSettingsProvider;
        private readonly ICandidateFileService _candidateFileService = candidateFileService;
        private readonly IWishListReader _wishListReader = wishListReader;
        private readonly IWishListResultWriter _wishListWriter = wishListWriter;
        private readonly IHistoricalCache _historicalCache = historicalCache;
        private readonly IFeatureEngine _featureEngine = featureEngine;
        private readonly NormalizeEvaluationsCommand _normalizeEvaluationsCommand = normalizeEvaluationsCommand;
        private readonly IEvaluationDatasetCsvService _evaluationDatasetCsvService = evaluationDatasetCsvService;
        private readonly IAgentPathService _pathService = pathService;
        private readonly ITextLogger _logger = logger;

        public async Task RunAsync()
        {
            var settings = _normalizeReportsSettingsProvider.Get();
            _logger.Info(
                $"Normalize reports started. Candidates={settings.Candidates} WishList={settings.WishList} Evaluations={settings.Evaluations} EvaluationDataset={settings.EvaluationDataset}");

            if (settings.Candidates)
                await NormalizeCandidatesAsync();

            if (settings.WishList)
                await NormalizeWishListAsync();

            if (settings.Evaluations)
                await NormalizeEvaluationsAsync();

            if (settings.EvaluationDataset)
                await NormalizeEvaluationDatasetAsync();

            _logger.Info(
                $"Reports normalized. Candidates={settings.Candidates} WishList={settings.WishList} Evaluations={settings.Evaluations} EvaluationDataset={settings.EvaluationDataset}");
        }

        private async Task NormalizeWishListAsync()
        {
            var path = _pathService.GetWishListFile();
            _logger.Info($"Wish list report normalization started: {path}");

            var items = await _wishListReader.ReadAsync(path);
            await _wishListWriter.WriteAsync(path, items);

            _logger.Info($"Wish list report normalized: Count={items.Count}");
        }

        private async Task NormalizeCandidatesAsync()
        {
            var path = _pathService.GetCandidatesFile();
            _logger.Info($"Candidates report normalization started: {path}");

            var document = await _candidateFileService.ReadAsync(path);
            _logger.Info(
                $"Candidates report loaded: Reversal={document.Candidates.Count}, TodayResearchLike={document.SameDayCandidates.Count}");

            var recalculated = RecalculateCandidateBollingerBandSeries(document);
            var latestScanTime = document.Candidates
                .Concat(document.SameDayCandidates)
                .Select(x => (DateTime?)x.Scan.ScanTime)
                .Max();

            await _candidateFileService.WriteAsync(
                path,
                document,
                document.Candidates
                    .Concat(document.SameDayCandidates)
                    .Where(x => IsCurrentScanOutput(x, latestScanTime)));

            _logger.Info(
                $"Candidates report normalized: Reversal={document.Candidates.Count}, TodayResearchLike={document.SameDayCandidates.Count}, BollingerBandSeriesRecalculated={recalculated}");
        }

        private int RecalculateCandidateBollingerBandSeries(CandidateFileDocument document)
        {
            var recalculated = 0;
            var processed = 0;
            var allCandidates = document.Candidates.Concat(document.SameDayCandidates).ToList();

            _logger.Info($"Candidate Bollinger band series recalculation started: Total={allCandidates.Count}");

            foreach (var candidate in allCandidates)
            {
                processed++;

                if (!_historicalCache.TryLoad(candidate.Ticker, Timeframe.H4, out var candles) ||
                    candles == null ||
                    candles.Count == 0)
                {
                    continue;
                }

                var ordered = candles
                    .Where(x => x.Time <= candidate.Scan.ScanTime)
                    .OrderBy(x => x.Time)
                    .ToList();

                if (ordered.Count == 0)
                    continue;

                var scanIndex = ordered.Count - 1;

                candidate.RecentDailyBbUpperBandSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyBollingerUpperBand);
                candidate.RecentDailyBbMidBandSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyBollingerMidBand);
                candidate.RecentDailyBbLowerBandSeries = BuildRecentDailySeries(ordered, scanIndex, x => x.DailyBollingerLowerBand);
                candidate.RecentWeeklyBbUpperBandSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyBollingerUpperBand);
                candidate.RecentWeeklyBbMidBandSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyBollingerMidBand);
                candidate.RecentWeeklyBbLowerBandSeries = BuildRecentWeeklySeries(ordered, scanIndex, x => x.WeeklyBollingerLowerBand);
                candidate.RecentH4BbUpperBandSeries = BuildRecentH4Series(ordered, scanIndex, x => x.H4BollingerUpperBand);
                candidate.RecentH4BbMidBandSeries = BuildRecentH4Series(ordered, scanIndex, x => x.H4BollingerMidBand);
                candidate.RecentH4BbLowerBandSeries = BuildRecentH4Series(ordered, scanIndex, x => x.H4BollingerLowerBand);

                recalculated++;

                if (processed % 100 == 0 || processed == allCandidates.Count)
                {
                    _logger.Info(
                        $"Candidate Bollinger band series recalculation progress: {processed}/{allCandidates.Count}, Recalculated={recalculated}");
                }
            }

            return recalculated;
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

        private static DateTime StartOfWeek(DateTime time)
        {
            var day = (int)time.DayOfWeek;
            var delta = day == 0 ? 6 : day - 1;
            return time.Date.AddDays(-delta);
        }

        private static DateTime StartOfH4Bucket(DateTime time)
            => new(time.Year, time.Month, time.Day, (time.Hour / 4) * 4, 0, 0, time.Kind);

        private static decimal Round(decimal value)
            => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

        private static bool IsCurrentScanOutput(CandidateDetails candidate, DateTime? latestScanTime)
            => latestScanTime.HasValue && candidate.Scan.ScanTime == latestScanTime.Value;

        private async Task NormalizeEvaluationsAsync()
        {
            await _normalizeEvaluationsCommand.RunAsync();
        }

        private async Task NormalizeEvaluationDatasetAsync()
        {
            var path = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), "datasets", "evaluation-dataset.csv"));
            var rows = await _evaluationDatasetCsvService.ReadAsync(path);
            await _evaluationDatasetCsvService.WriteAsync(path, rows);

            _logger.Info($"Evaluation dataset report normalized: Rows={rows.Count}");
        }
    }
}
