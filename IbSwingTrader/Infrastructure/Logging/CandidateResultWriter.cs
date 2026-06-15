using IbSwingTrader.Common.Time;
using IbSwingTrader.Domain.Settings;

namespace IbSwingTrader.Infrastructure.Logging
{
    public class CandidateResultWriter(
        IGetCandidatesSettingsProvider getCandidatesSettingsProvider,
        IMarketSettingsProvider marketSettingsProvider,
        ITextLogger logger,
        IConsoleColorWriter console,
        ICandidateFileService candidateFileService,
        INumberTextFormatter fmt) : ICandidateResultWriter
    {
        private readonly IGetCandidatesSettingsProvider _getCandidatesSettingsProvider = getCandidatesSettingsProvider;
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;
        private readonly ITextLogger _logger = logger;
        private readonly IConsoleColorWriter _console = console;
        private readonly ICandidateFileService _candidateFileService = candidateFileService;
        private readonly INumberTextFormatter _fmt = fmt;

        public async Task WriteAsync(string filePath, CandidateSearchResult result)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(result);

            var candidates = OrderPrimaryForDisplay(result.Candidates);
            var sameDayCandidates = OrderSameDayForDisplay(result.SameDayCandidates);

            var folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            var marketSettings = _marketSettingsProvider.Get();
            var scanTime = GetMarketNow(marketSettings.Timezone);

            if (sameDayCandidates.Count > 0)
                WriteConsoleSectionHeader("RunawayCandidates");

            foreach (var candidate in sameDayCandidates)
            {
                candidate.Scan.ScanTime = scanTime;
                candidate.Scan.ScanTimeZone = marketSettings.Timezone;

                WriteCandidateToConsole(candidate);
            }

            WriteConsoleSectionHeader("ReversalCandidates");
            foreach (var candidate in candidates)
            {
                candidate.Scan.ScanTime = scanTime;
                candidate.Scan.ScanTimeZone = marketSettings.Timezone;

                WriteCandidateToConsole(candidate);
            }

            var existingDocument = await _candidateFileService.ReadAsync(filePath);
            var mergedSameDay = MergeCandidates(existingDocument.SameDayCandidates, sameDayCandidates);
            var merged = MergeCandidates(existingDocument.Candidates, candidates)
                .Where(x => !mergedSameDay.Any(y =>
                    string.Equals(BuildCandidateScanKey(y), BuildCandidateScanKey(x), StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var summary = BuildSummary(candidates, sameDayCandidates);
            await _candidateFileService.WriteAsync(
                filePath,
                new CandidateFileDocument
                {
                    Summary = summary,
                    Candidates = merged,
                    SameDayCandidates = mergedSameDay
                },
                candidates.Concat(sameDayCandidates));

            _logger.Info($"Candidate results saved: {filePath}");
        }

        private static List<CandidateDetails> MergeCandidates(
            List<CandidateDetails> existing,
            List<CandidateDetails> incoming)
        {
            var map = new Dictionary<string, CandidateDetails>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in existing)
                UpsertCandidate(map, item, preferIncomingOnSameScan: false);

            foreach (var item in incoming)
                UpsertCandidate(map, item, preferIncomingOnSameScan: true);

            return map.Values
                .OrderByDescending(x => x.Scan.ScanTime)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void UpsertCandidate(
            Dictionary<string, CandidateDetails> map,
            CandidateDetails candidate,
            bool preferIncomingOnSameScan)
        {
            var key = BuildCandidateOperationKey(candidate);

            if (!map.TryGetValue(key, out var existing))
            {
                map[key] = candidate;
                return;
            }

            var keepIncoming =
                candidate.Scan.ScanTime > existing.Scan.ScanTime ||
                (candidate.Scan.ScanTime == existing.Scan.ScanTime &&
                 preferIncomingOnSameScan &&
                 candidate.Score.Score >= existing.Score.Score);

            if (keepIncoming)
                map[key] = candidate;
        }

        private static string BuildCandidateOperationKey(CandidateDetails candidate)
        {
            return CandidateCsvRowBuilder.BuildCandidateOperationKey(candidate);
        }

        private static string BuildCandidateScanKey(CandidateDetails candidate)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{candidate.Ticker}|{candidate.Scan.PresetScanCode}|{candidate.Scan.ScanTime:O}");
        }

        private CandidateSummarySections BuildSummary(
            IEnumerable<CandidateDetails> candidates,
            IEnumerable<CandidateDetails> sameDayCandidates)
        {
            var orderedCandidates = OrderPrimaryForDisplay(candidates);
            var premarketSummarySettings = _getCandidatesSettingsProvider.Get().PremarketSummary;
            var orderedSameDayCandidates = OrderSameDayForDisplay(sameDayCandidates)
                .Take(premarketSummarySettings.MaxItems)
                .ToList();
            return new CandidateSummarySections
            {
                RunawayCandidates = orderedSameDayCandidates
                    .Select(x => BuildSummaryItem(x, includeSameDayMarker: true))
                    .ToList(),
                ReversalCandidates = orderedCandidates
                    .Select(x => BuildSummaryItem(x, includeSameDayMarker: false))
                    .ToList()
            };
        }

        private static List<CandidateDetails> OrderPrimaryForDisplay(IEnumerable<CandidateDetails> candidates)
        {
            return candidates
                .OrderByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
                .ThenByDescending(x => x.TradePlan.ProfitPercent)
                .ThenByDescending(x => x.Score.Score)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<CandidateDetails> OrderSameDayForDisplay(IEnumerable<CandidateDetails> candidates)
        {
            return candidates
                .OrderByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
                .ThenByDescending(x => x.TradePlan.ProfitPercent)
                .ThenByDescending(x => x.Score.Score)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private CandidateSummaryItem BuildSummaryItem(CandidateDetails candidate, bool includeSameDayMarker)
        {
            var markers = BuildSummaryMarkers(candidate, includeSameDayMarker);
            var stopLimitPrice = ResolveStopLimitPrice(candidate);
            var ticker =
                $"{candidate.Ticker} " +
                $"{_fmt.Price(candidate.TradePlan.EntryPrice)} " +
                $"{_fmt.Price(candidate.TradePlan.ExitPrice)} " +
                $"{_fmt.Price(candidate.TradePlan.StopLoss)}/{_fmt.Price(stopLimitPrice)} " +
                $"{_fmt.Percent(candidate.TradePlan.ProfitPercent)}%/" +
                $"{_fmt.Percent(candidate.TradePlan.LossPercent)}%" +
                $" rank={_fmt.Generic(candidate.Score.NextDayRank ?? 0m)}" +
                $"{markers}";

            return new CandidateSummaryItem
            {
                Ticker = ticker
            };
        }

        private string BuildSummaryMarkers(CandidateDetails candidate, bool includeSameDayMarker)
        {
            var markers = new List<string>();

            if (includeSameDayMarker)
                markers.Add("runaway");

            if (candidate.NeedsDeeperEntry)
                markers.Add("deep-entry");

            if (candidate.NeedsMomentumExit)
                markers.Add("momentum-exit");

            if (!string.IsNullOrWhiteSpace(candidate.WeeklyBbRegime) &&
                !candidate.WeeklyBbRegime.Equals("Neutral", StringComparison.OrdinalIgnoreCase))
            {
                markers.Add($"w-{candidate.WeeklyBbRegime.ToLowerInvariant()}");
            }

            if (!string.IsNullOrWhiteSpace(candidate.DailyBbRegime) &&
                !candidate.DailyBbRegime.Equals("Neutral", StringComparison.OrdinalIgnoreCase))
            {
                markers.Add($"d-{candidate.DailyBbRegime.ToLowerInvariant()}");
            }

            if (!string.IsNullOrWhiteSpace(candidate.H4BbRegime) &&
                !candidate.H4BbRegime.Equals("Neutral", StringComparison.OrdinalIgnoreCase))
            {
                markers.Add($"h4-{candidate.H4BbRegime.ToLowerInvariant()}");
            }

            if (IsParabolicExpansionProxy(candidate))
            {
                markers.Add("parabolic-expansion");
            }
            else if (IsDeepParabolicExpansionProxy(candidate))
            {
                markers.Add("deep-parabolic");
            }
            else if (IsExplosiveMinFirstProxy(candidate))
            {
                markers.Add("minfirst");
                markers.Add("explosive");
            }
            else if (IsConstructiveDeepMinFirstProxy(candidate))
            {
                markers.Add("minfirst");
                markers.Add("constructive-deep");
            }
            else if (IsStrongMinFirstProxy(candidate))
            {
                markers.Add("minfirst");
                markers.Add("strong");
            }
            else if (IsWeakDeepPullbackProxy(candidate))
            {
                markers.Add("minfirst");
                markers.Add("weak-deep");
            }

            return markers.Count == 0
                ? string.Empty
                : $" {string.Join(" ", markers)}";
        }

        private bool IsStrongMinFirstProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.StrongMinFirstExit;
            if (!settings.Enabled || candidate.NeedsDeeperEntry || candidate.NeedsMomentumExit)
                return false;

            return diagnostics.DailyTrendPosition >= settings.DailyTrendPositionThreshold &&
                   diagnostics.TrendPosition >= settings.TrendPositionThreshold &&
                   diagnostics.ATRRatio <= settings.MaxAtrRatio &&
                   context.DistanceTo20dHigh <= settings.MaxDistanceTo20dHigh;
        }

        private bool IsWeakDeepPullbackProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.WeakDeepPullbackExit;
            if (!settings.Enabled || !candidate.NeedsDeeperEntry)
                return false;

            var constructiveSettings = _getCandidatesSettingsProvider.Get().TradePlan.ConstructiveDeepMinFirst;
            var isConstructive =
                constructiveSettings.Enabled &&
                diagnostics.DailyTrendPosition >= constructiveSettings.MinDailyTrendPosition &&
                diagnostics.TrendPosition >= constructiveSettings.MinTrendPosition &&
                diagnostics.ATRRatio >= constructiveSettings.MinAtrRatio &&
                context.DailyRSI14 >= constructiveSettings.MinDailyRsi14;

            if (isConstructive)
                return false;

            return diagnostics.DailyTrendPosition <= settings.MaxDailyTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio;
        }

        private bool IsExplosiveMinFirstProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.ExplosiveMinFirstExit;
            if (!settings.Enabled || candidate.NeedsDeeperEntry || !candidate.NeedsMomentumExit)
                return false;

            if (IsParabolicExpansionProxy(candidate))
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   context.DailyRSI14 >= settings.MinDailyRsi14 &&
                   context.DistanceTo20dHigh <= settings.MaxDistanceTo20dHigh;
        }

        private bool IsConstructiveDeepMinFirstProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.ConstructiveDeepMinFirst;
            if (!settings.Enabled || !candidate.NeedsDeeperEntry)
                return false;

            if (IsDeepParabolicExpansionProxy(candidate))
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   context.DailyRSI14 >= settings.MinDailyRsi14;
        }

        private bool IsParabolicExpansionProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.ParabolicExpansionExit;
            if (!settings.Enabled || candidate.NeedsDeeperEntry || !candidate.NeedsMomentumExit)
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   context.DailyRSI14 >= settings.MinDailyRsi14 &&
                   context.DistanceTo20dHigh >= settings.MaxDistanceTo20dHigh &&
                   diagnostics.VolumeRatio20 >= settings.MinVolumeRatio20;
        }

        private bool IsDeepParabolicExpansionProxy(CandidateDetails candidate)
        {
            var diagnostics = candidate.Diagnostics;
            var context = candidate.Context;
            if (diagnostics == null || context == null)
                return false;

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.DeepParabolicExpansionExit;
            if (!settings.Enabled || !candidate.NeedsDeeperEntry)
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   context.DailyRSI14 >= settings.MinDailyRsi14 &&
                   diagnostics.VolumeRatio20 >= settings.MinVolumeRatio20;
        }

        private void WriteCandidateToConsole(CandidateDetails candidate)
        {
            var stopLimitPrice = ResolveStopLimitPrice(candidate);
            _console.Write($"{candidate.Ticker} ", ConsoleColor.Gray);
            _console.Write($"{_fmt.Price(candidate.TradePlan.EntryPrice)} ", ConsoleColor.DarkYellow);
            _console.Write($"{_fmt.Price(candidate.TradePlan.ExitPrice)} ", ConsoleColor.DarkGreen);
            _console.Write($"{_fmt.Price(candidate.TradePlan.StopLoss)}/{_fmt.Price(stopLimitPrice)} ", ConsoleColor.DarkRed);
            _console.Write($"{_fmt.Percent(candidate.TradePlan.ProfitPercent)}%", ConsoleColor.Green);
            _console.Write("/", ConsoleColor.DarkGray);
            _console.Write($"{_fmt.Percent(candidate.TradePlan.LossPercent)}%", ConsoleColor.Red);
            _console.WriteLine(string.Empty, ConsoleColor.Gray);
        }

        private void WriteConsoleSectionHeader(string title)
        {
            _console.WriteLine(string.Empty, ConsoleColor.Gray);
            _console.WriteLine(title, ConsoleColor.Cyan);
        }

        private static decimal ResolveStopLimitPrice(CandidateDetails candidate)
        {
            return candidate.TradePlan.StopLimitPrice > 0m
                ? candidate.TradePlan.StopLimitPrice
                : candidate.TradePlan.StopLoss;
        }

        private static DateTime GetMarketNow(string timezoneId)
        {
            return MarketTime.Now(timezoneId);
        }
    }
}
