using System.Globalization;
using IBApi;
using IbSwingTrader.Common.Time;
using IbSwingTrader.Domain.Settings;

namespace IbSwingTrader.Application.Candidates
{
    public class CandidateFinder(
        IStockUniverseProvider stockUniverseProvider,
        IStockPreFilter preFilter,
        IContractResolver contractResolver,
        IHistoricalCache historicalCache,
        IHistoricalDataService historicalData,
        IFeatureEngine featureEngine,
        ICandidateSignalAnalyzer signalAnalyzer,
        IBollingerFigureAnalyzer bollingerFigureAnalyzer,
        IWishListScore wishListScore,
        ICandidateScore candidateScore,
        ITradeBuilder tradeBuilder,
        IScanCodeInfoService scannerPresets,
        IMarketSettingsProvider marketSettingsProvider,
        IGetCandidatesSettingsProvider getCandidatesSettingsProvider,
        INumberTextFormatter fmt,
        ITextLogger logger) : ICandidateFinder
    {
        private readonly IStockUniverseProvider _stockUniverseProvider = stockUniverseProvider;
        private readonly IStockPreFilter _preFilter = preFilter;
        private readonly IContractResolver _contractResolver = contractResolver;
        private readonly IHistoricalCache _historicalCache = historicalCache;
        private readonly IHistoricalDataService _historicalData = historicalData;
        private readonly IFeatureEngine _featureEngine = featureEngine;
        private readonly ICandidateSignalAnalyzer _signalAnalyzer = signalAnalyzer;
        private readonly IBollingerFigureAnalyzer _bollingerFigureAnalyzer = bollingerFigureAnalyzer;
        private readonly IWishListScore _wishListScore = wishListScore;
        private readonly ICandidateScore _candidateScore = candidateScore;
        private readonly ITradeBuilder _tradeBuilder = tradeBuilder;
        private readonly IScanCodeInfoService _scannerPresets = scannerPresets;
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;
        private readonly IGetCandidatesSettingsProvider _getCandidatesSettingsProvider = getCandidatesSettingsProvider;
        private readonly INumberTextFormatter _fmt = fmt;
        private readonly ITextLogger _logger = logger;
        private readonly NextDayRankingSettings _nextDayRankingSettings = getCandidatesSettingsProvider.Get().NextDayRanking;
        private DateTime? _expectedLatestClosedDailyDate;
        private const int RecentDailySeriesLength = RecentSeriesWindow.Daily;
        private const int RecentWeeklySeriesLength = RecentSeriesWindow.Weekly;
        private const int RecentH4SeriesLength = RecentSeriesWindow.H4;
        private static readonly TimeSpan RegularSessionEnd = new(16, 0, 0);
        private static readonly TimeSpan PreMarketSessionStart = new(4, 0, 0);

        public async Task<CandidateSearchResult> FindAsync()
        {
            var getCandidatesSettings = _getCandidatesSettingsProvider.Get();
            var finderSettings = getCandidatesSettings.Finder;
            var contractResolveTimeout = TimeSpan.FromSeconds(
                Math.Max(15, finderSettings.ContractResolveTimeoutSeconds));
            var contractResolveMaxAttempts = Math.Max(1, finderSettings.ContractResolveMaxAttempts);

            var marketTimezone = _marketSettingsProvider.Get().Timezone;
            var marketNow = GetMarketNow(marketTimezone);
            var firstSeen = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            _logger.Info(
                $"CandidateFinder settings: " +
                $"LookbackCalendarDays={finderSettings.LookbackCalendarDays}, " +
                $"MinimumCandles={finderSettings.MinimumCandles}, " +
                $"AvgVolumePeriod={finderSettings.AvgVolumePeriod}, " +
                $"CandleCount={finderSettings.CandleCount}, " +
                $"ContractResolveTimeoutSeconds={finderSettings.ContractResolveTimeoutSeconds}, " +
                $"ContractResolveMaxAttempts={finderSettings.ContractResolveMaxAttempts}");

            var emitAllSeenCandidates = finderSettings.EmitAllSeenCandidates;
            var scannedWishListContexts = new Dictionary<string, WishListContext>(StringComparer.OrdinalIgnoreCase);
            var allScannedWishListContexts = new List<WishListContext>();
            var candidateResults = new Dictionary<string, CandidateDetails>(StringComparer.OrdinalIgnoreCase);
            var performance = new ScanPerformanceSummary();
            foreach (var preset in _scannerPresets.GetAll())
            {
                var stageMetric = performance.BeginStage(preset.ScanCode);
                var stocks = await _stockUniverseProvider.GetStocksAsync(preset.ScanCode);
                var receivedAt = MarketTime.Now();
                foreach (var stock in stocks)
                    firstSeen.TryAdd(stock.Ticker, receivedAt);
                stageMetric.Input = stocks.Count;

                foreach (var stock in stocks)
                {
                    var tickerMetric = performance.BeginTicker(preset.ScanCode, stock.Ticker);
                    try
                    {
                        stageMetric.Processed++;

                        if (!_preFilter.Pass(stock))
                        {
                            stageMetric.Skipped++;
                            tickerMetric.Skipped = true;
                            continue;
                        }

                        stageMetric.UniqueTickers.Add(stock.Ticker);

                        Contract? contract = null;
                        List<Candle>? candles;

                        if (TryLoadPreparedH4CandlesFromCache(stock.Ticker, finderSettings, out candles))
                        {
                            stageMetric.CacheHits++;
                            tickerMetric.CacheHits++;
                        }
                        else
                        {
                            try
                            {
                                contract = await _contractResolver.ResolveStockAsync(
                                    stock.Ticker,
                                    contractResolveTimeout,
                                    contractResolveMaxAttempts);

                                var end = MarketTime.Now();
                                var start = end.AddDays(-finderSettings.LookbackCalendarDays);

                                stageMetric.HistoricalLoads++;
                                tickerMetric.HistoricalLoads++;
                                candles = await _historicalData.GetCandlesRange(
                                    stock.Ticker,
                                    contract,
                                    Timeframe.H4,
                                    start,
                                    end);

                                candles = PrepareFinderCandles(stock.Ticker, candles, finderSettings);
                            }
                            catch (Exception ex)
                            {
                                stageMetric.Skipped++;
                                tickerMetric.Skipped = true;
                                tickerMetric.Errors++;
                                _logger.Info($"Skipping {stock.Ticker}: failed to load candles. {ex.Message}");
                                continue;
                            }
                        }

                        if (candles == null || candles.Count < finderSettings.MinimumCandles)
                        {
                            stageMetric.Skipped++;
                            tickerMetric.Skipped = true;
                            _logger.Info(
                                $"Skipping {stock.Ticker}: not enough candles " +
                                $"({candles?.Count ?? 0} < {finderSettings.MinimumCandles}).");
                            continue;
                        }

                        List<Candle>? dailyCandles = null;

                        try
                        {
                            contract ??= await _contractResolver.ResolveStockAsync(
                                stock.Ticker,
                                contractResolveTimeout,
                                contractResolveMaxAttempts);

                            var end = MarketTime.Now();
                            var start = end.AddDays(-finderSettings.LookbackCalendarDays);

                            stageMetric.HistoricalLoads++;
                            tickerMetric.HistoricalLoads++;
                            dailyCandles = await _historicalData.GetCandlesRange(
                                stock.Ticker,
                                contract,
                                Timeframe.D1,
                                start,
                                end);
                        }
                        catch (Exception ex)
                        {
                            tickerMetric.Errors++;
                            _logger.Info($"Daily candles load skipped for {stock.Ticker}. {ex.Message}");
                        }

                        var dailyBars = dailyCandles ?? BuildDailyBars(candles);
                        var weeklyBars = BuildWeeklyBars(candles);

                        _logger.Info(
                            $"Ticker history prepared: {stock.Ticker}. " +
                            $"H4={candles.Count}, D1={dailyBars.Count}, W1={weeklyBars.Count}");

                        if (TryRejectByRecentDailyPriceFloor(stock.Ticker, dailyBars, out var recentPriceFloorReason))
                        {
                            stageMetric.Skipped++;
                            tickerMetric.Skipped = true;
                            _logger.Info($"Skipping {stock.Ticker}: {recentPriceFloorReason}");
                            continue;
                        }

                        CandidateSignalSnapshot snapshot;

                        try
                        {
                            snapshot = _signalAnalyzer.Analyze(candles);
                        }
                        catch (Exception ex)
                        {
                            stageMetric.Skipped++;
                            tickerMetric.Skipped = true;
                            tickerMetric.Errors++;
                            _logger.Info($"Skipping {stock.Ticker}: failed to analyze signals. {ex.Message}");
                            continue;
                        }

                        var lastPrice = candles[^1].Close;
                        var avgDollarVolume = CalculateAverageDollarVolumeDaily(candles, finderSettings.AvgVolumePeriod);

                        _logger.Info(
                            $"Processing ticker ({stock.Ticker}), " +
                            $"preset ({preset.ScanCode}), " +
                            $"stock type ({stock.StockType}), " +
                            $"trading class ({stock.TradingClass}), " +
                            $"exchange ({stock.Exchange}), " +
                            $"rank ({stock.Rank}), " +
                            $"avgDollarVolume={_fmt.Generic(avgDollarVolume)}");

                        var diagnostics = BuildDiagnostics(snapshot, candles);
                        var entryScore = _candidateScore.Calculate(snapshot);
                        var bbState = BuildBollingerStateSet(BuildRecentFeatureSeries(candles));

                        var wishScore = _wishListScore.Calculate(snapshot);

                        var wishListItem = BuildWishListItem(
                            stock,
                            preset,
                            snapshot,
                            candles,
                            marketNow,
                            marketTimezone,
                            wishScore);

                        var scanContext = new WishListContext
                        {
                            Stock = stock,
                            Contract = contract,
                            Preset = preset,
                            Snapshot = snapshot,
                            Candles = candles,
                            DailyCandles = dailyCandles,
                            ChartH4Candles = TryLoadSessionAlignedH4Candles(stock.Ticker, marketNow),
                            ScanTimeMarket = MarketTime.Now(),
                            AvgDollarVolumeDaily = avgDollarVolume,
                            WishListItem = wishListItem
                        };

                        allScannedWishListContexts.Add(scanContext);
                        stageMetric.Added++;
                        tickerMetric.Added = true;

                        AddOrReplaceWishListContext(
                            scannedWishListContexts,
                            scanContext);
                    }
                    finally
                    {
                        tickerMetric.Stop();
                    }
                }

                stageMetric.Stop();
            }

            var contextsForOutput = emitAllSeenCandidates
                ? allScannedWishListContexts
                : scannedWishListContexts.Values.ToList();
            var scannedWishListItems = contextsForOutput
                .Select(x => x.WishListItem)
                .ToList();

            var mergedWishList = scannedWishListItems;
            _expectedLatestClosedDailyDate = ResolveExpectedLatestClosedDailyDate(
                contextsForOutput,
                marketNow.Date);

            _logger.Info(
                $"Current scan contexts prepared. Total={mergedWishList.Count}, " +
                $"UniqueTickers={scannedWishListContexts.Count}, " +
                $"EmitAllSeenCandidates={emitAllSeenCandidates}, " +
                $"ExpectedLatestClosedDailyDate={GetExpectedLatestClosedDailyDate(marketNow.Date):yyyy-MM-dd}");

            var sameDayPromotedResults = new Dictionary<string, CandidateDetails>(StringComparer.OrdinalIgnoreCase);

            var rankingStageMetric = performance.BeginStage("Ranking/Rebuild");
            rankingStageMetric.Input = contextsForOutput.Count;
            foreach (var ctx in contextsForOutput)
            {
                rankingStageMetric.Processed++;
                rankingStageMetric.UniqueTickers.Add(ctx.Stock.Ticker);
                var rankingTickerMetric = performance.BeginTicker("Ranking/Rebuild", ctx.Stock.Ticker);
                ctx.PerformanceMetric = rankingTickerMetric;
                var scanItem = ctx.WishListItem;

                var dailyFamilySplit = ClassifyDailyFamily(ctx, log: false);
                var target = dailyFamilySplit == DailyFamilySplit.Reversal
                    ? candidateResults
                    : sameDayPromotedResults;
                var countBefore = target.Count;
                try
                {
                    await TryAddCandidate(
                        target,
                        scanItem,
                        ctx,
                        isFromWishlist: dailyFamilySplit == DailyFamilySplit.Reversal,
                        marketTimezone,
                        bucketName: dailyFamilySplit == DailyFamilySplit.Reversal
                            ? "reversal candidates"
                            : "runaway candidates",
                        rejectionLogPrefix: "Pattern rejected");
                }
                finally
                {
                    rankingTickerMetric.Stop();
                    ctx.PerformanceMetric = null;
                }

                if (target.Count > countBefore)
                {
                    rankingStageMetric.Added++;
                    rankingTickerMetric.Added = true;
                }
                else
                {
                    rankingStageMetric.Skipped++;
                    rankingTickerMetric.Skipped = true;
                }
            }
            rankingStageMetric.Stop();

            var sameDayCandidates = emitAllSeenCandidates
                ? sameDayPromotedResults.Values.ToList()
                : sameDayPromotedResults.Values
                    .GroupBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x
                        .OrderByDescending(y => GetCandidateSourcePriority(y))
                        .ThenByDescending(y => y.Score.NextDayRank ?? decimal.MinValue)
                        .ThenByDescending(y => y.Score.Score)
                        .First())
                    .ToList();

            sameDayCandidates = ReRankCandidates(
                sameDayCandidates,
                SeriesTemplateFamily.TodayResearchLike);

            var finalCandidates = ReRankCandidates(
                candidateResults.Values
                    .ToList(),
                SeriesTemplateFamily.Reversal);

            if (emitAllSeenCandidates)
                (finalCandidates, sameDayCandidates) = DeduplicateCurrentScanByTicker(
                    finalCandidates,
                    sameDayCandidates);

            foreach (var candidate in finalCandidates.Concat(sameDayCandidates))
            {
                candidate.Scan.RunStartedAt = marketNow;
                if (firstSeen.TryGetValue(candidate.Ticker, out var seenAt))
                    candidate.Scan.FirstSeenAt = seenAt;
                candidate.Scan.SignalObservedAt = candidate.Scan.ScanTime;
            }

            // Refresh only the deduplicated, playable BellUp plans. No requests for
            // hundreds of diagnostic rejects, and no change to Reversal entry policy.
            foreach (var candidate in sameDayCandidates.Where(x => !CandidateGroups.IsOther(x) && x.IsBellUpPattern).Reverse())
            {
                var ctx = contextsForOutput.First(x =>
                    string.Equals(x.Stock.Ticker, candidate.Ticker, StringComparison.OrdinalIgnoreCase) &&
                    x.Preset.ScanCode == candidate.Scan.PresetScanCode);
                await RefreshBellUpForPublication(candidate, ctx);
            }

            LogScanPerformanceSummary(performance);

            return new CandidateSearchResult
            {
                Candidates = [.. finalCandidates],
                SameDayCandidates = sameDayCandidates,
                WishList = []
            };
        }

        private async Task RefreshBellUpForPublication(CandidateDetails candidate, WishListContext ctx)
        {
            var initial = candidate.TradePlan;
            try
            {
                if (ctx.Contract == null)
                    throw new InvalidOperationException("Contract unavailable for fresh price");

                var requestedAt = MarketTime.Now();
                var start = candidate.Scan.FirstSeenAt ?? requestedAt.AddMinutes(-15);
                start = start.AddTicks(-(start.Ticks % TimeSpan.FromMinutes(5).Ticks)).AddMinutes(-5);
                var bars = await _historicalData.GetFreshM5Snapshot(
                    candidate.Ticker, ctx.Contract, start, requestedAt);
                var quote = ExecutionPriceSnapshot.FromM5(bars, requestedAt, MarketTime.Now());
                if (quote == null)
                    throw new InvalidOperationException("No current or immediately preceding M5 bar");

                var refreshed = await BuildTradePlan(ctx, quote);
                refreshed.InitialReferencePrice = initial.LiveReferencePrice;
                refreshed.InitialReferencePriceTime = initial.ReferencePriceTime;
                refreshed.InitialReferencePriceBarTime = initial.ReferencePriceBarTime;
                refreshed.InitialReferencePriceObservedAt = initial.ReferencePriceObservedAt;
                refreshed.InitialEntryPrice = initial.EntryPrice;
                refreshed.PublicationRefreshStatus = "Refreshed";
                candidate.TradePlan = refreshed;

                if (IsLiveRunawayStructureInvalidated(quote.Price, ctx.Candles,
                    BuildRecentFeatureSeries(ctx.Candles), out var reason))
                    throw new InvalidOperationException($"Live structure invalidated: {reason}");

                _logger.Info($"Publication plan refreshed: {candidate.Ticker}. " +
                    $"FirstSeenAt={candidate.Scan.FirstSeenAt:O}, PriceTime={quote.PriceTime:O}, " +
                    $"ObservedAt={quote.ObservedAt:O}, Source={quote.Source}, " +
                    $"OldEntry={initial.EntryPrice}, Entry={refreshed.EntryPrice}, " +
                    $"Exit={refreshed.ExitPrice}, Stop={refreshed.StopLoss}, " +
                    $"ShadowM5Entry={quote.ProjectedEntryPrice}");
            }
            catch (Exception ex)
            {
                // Preserve diagnostics, but do not publish a stale plan as executable.
                var plan = candidate.TradePlan;
                plan.InitialReferencePrice = initial.LiveReferencePrice;
                plan.InitialReferencePriceTime = initial.ReferencePriceTime;
                plan.InitialReferencePriceBarTime = initial.ReferencePriceBarTime;
                plan.InitialReferencePriceObservedAt = initial.ReferencePriceObservedAt;
                plan.InitialEntryPrice = initial.EntryPrice;
                plan.PublicationRefreshStatus = "Unavailable";
                plan.EntryPrice = plan.ExitPrice = plan.StopLoss = plan.StopLimitPrice = 0m;
                plan.ProfitPercent = plan.LossPercent = 0m;
                plan.ExitProfile = null;
                candidate.CandidateSource = "Other";
                candidate.PatternVerdictReason += "; Publication refresh unavailable";
                candidate.Context.Notes = AppendDiagnosticNote(candidate.Context.Notes, ex.Message);
                _logger.Info($"Publication plan rejected: {candidate.Ticker}. {ex.Message}");
            }
        }

        private (List<CandidateDetails> FinalCandidates, List<CandidateDetails> SameDayCandidates)
            DeduplicateCurrentScanByTicker(
                List<CandidateDetails> finalCandidates,
                List<CandidateDetails> sameDayCandidates)
        {
            var candidates = finalCandidates
                .Select(x => new CandidateGroupItem(x, IsSameDay: false))
                .Concat(sameDayCandidates.Select(x => new CandidateGroupItem(x, IsSameDay: true)))
                .ToList();

            if (candidates.Count <= 1)
                return (finalCandidates, sameDayCandidates);

            var selected = candidates
                .GroupBy(x => x.Candidate.Ticker, StringComparer.OrdinalIgnoreCase)
                .Select(x =>
                {
                    var scanCodes = x
                        .Select(y => y.Candidate.Scan.PresetScanCode)
                        .Where(y => !string.IsNullOrWhiteSpace(y))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(y => y, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    var best = x
                        .OrderByDescending(y => GetCandidateSourcePriority(y.Candidate))
                        .ThenByDescending(y => y.Candidate.Score.NextDayRank ?? decimal.MinValue)
                        .ThenByDescending(y => y.Candidate.TradePlan.ProfitPercent)
                        .ThenByDescending(y => y.Candidate.Score.Score)
                        .ThenBy(y => y.Candidate.Scan.PresetScanCode, StringComparer.OrdinalIgnoreCase)
                        .First();

                    if (scanCodes.Count > 1)
                    {
                        best.Candidate.Context.Notes = AppendDiagnosticNote(
                            best.Candidate.Context.Notes,
                            $"SeenScanCodes={string.Join(",", scanCodes)}");
                    }

                    return best;
                })
                .ToList();

            var dedupedFinal = selected
                .Where(x => !x.IsSameDay)
                .Select(x => x.Candidate)
                .ToList();
            var dedupedSameDay = selected
                .Where(x => x.IsSameDay)
                .Select(x => x.Candidate)
                .ToList();

            _logger.Info(
                $"Current scan ticker de-duplication applied. " +
                $"Before={candidates.Count}, After={selected.Count}");

            return (dedupedFinal, dedupedSameDay);
        }

        private async Task<List<CandidateDetails>> BuildPremarketSummaryCandidates(
            List<WishListItem> mergedWishList,
            Dictionary<string, WishListItem> mergedMap,
            Dictionary<string, WishListContext> scannedWishListContexts,
            HashSet<string> promotedTickers,
            DateTime marketNow,
            string marketTimezone)
        {
            var settings = _getCandidatesSettingsProvider.Get().PremarketSummary;
            if (!settings.Enabled || settings.MaxItems <= 0)
                return [];

            var todayMarketDate = marketNow.Date;
            var results = new List<CandidateDetails>();

            foreach (var ctx in scannedWishListContexts.Values)
            {
                if (promotedTickers.Contains(ctx.Stock.Ticker))
                    continue;

                if (!mergedMap.TryGetValue(ctx.Stock.Ticker, out var mergedWishItem))
                    continue;

                var firstSeenDate = mergedWishItem.FirstSeen?.Date;
                if (firstSeenDate.HasValue && firstSeenDate.Value < todayMarketDate)
                    continue;

                var dailyFamilySplit = ClassifyDailyFamily(ctx, log: false);
                if (dailyFamilySplit == DailyFamilySplit.Unknown)
                    continue;

                var recentSeries = BuildRecentFeatureSeries(ctx.Candles);
                var diagnostics = BuildDiagnostics(ctx.Snapshot, ctx.Candles);
                var entryScore = _candidateScore.Calculate(ctx.Snapshot);
                var bbState = BuildBollingerStateSet(recentSeries);
                var isTodayResearchLikeCandidate = IsTodayResearchLikeCandidate(
                    mergedWishItem,
                    ctx,
                    diagnostics,
                    entryScore,
                    recentSeries,
                    bbState,
                    dailyFamilySplit,
                    out _);

                if (!isTodayResearchLikeCandidate ||
                    !IsBellUpPhaseReadyToday(ctx, ClassifyBellPatternSignal(bbState, recentSeries), out _))
                    continue;

                if (entryScore < settings.MinEntryScore && !isTodayResearchLikeCandidate)
                    continue;

                if (!ShouldBypassWishListFilterForLiveScan(ctx.Snapshot, diagnostics, entryScore) &&
                    !isTodayResearchLikeCandidate)
                    continue;

                var trade = ctx.Trade ??= await BuildTradePlan(ctx);
                if (trade.ProfitPercent < settings.MinPlannedProfitPct)
                {
                    _logger.Info(
                        $"Premarket summary candidate rejected: {ctx.Stock.Ticker}. " +
                        $"Planned profit is too small: ProfitPercent={_fmt.Percent(trade.ProfitPercent)}%, " +
                        $"MinRequired={_fmt.Percent(settings.MinPlannedProfitPct)}%");
                    continue;
                }

                if (!PassFinalAmplitudeProxyGate(ctx, out var amplitudeRejectReason))
                {
                    _logger.Info(
                        $"Premarket summary candidate rejected: {ctx.Stock.Ticker}. " +
                        amplitudeRejectReason);
                    continue;
                }

                var needsDeeperEntry = ResolveNeedsDeeperEntry(ctx.Snapshot, diagnostics);
                var needsMomentumExit = ResolveNeedsMomentumExit(ctx.Snapshot, diagnostics, entryScore);
                var dailyScore = mergedWishItem.Score.DailyScore ?? 0m;
                var weeklyScore = mergedWishItem.Score.WeeklyScore ?? 0m;
                var finalScore = dailyScore + weeklyScore + entryScore;
                var todayResearchLikePatternKind = ClassifyTodayResearchLikePatternKind(bbState, recentSeries);
                var todayResearchLikeSeriesScore = CalculateTodayResearchLikeSeriesScore(bbState, recentSeries);

                var candidateItem = BuildCandidateItem(
                    ctx.Stock,
                    isFromWishlist: false,
                    DailyFamilySplit.TodayResearchLike,
                    needsDeeperEntry,
                    needsMomentumExit,
                    ctx.Preset,
                    ctx.Snapshot,
                    ctx.Candles,
                    trade,
                    diagnostics,
                    ctx.ScanTimeMarket,
                    marketTimezone,
                    dailyScore,
                    weeklyScore,
                    entryScore,
                    finalScore,
                    todayResearchLikePatternKind,
                    todayResearchLikeSeriesScore);

                candidateItem.CandidateSource = "SameDayContinuation";
                results.Add(candidateItem);
            }

            var deduped = results
                .GroupBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderByDescending(y => y.Score.NextDayRank ?? decimal.MinValue)
                    .ThenByDescending(y => y.Score.Score)
                    .First())
                .OrderByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
                .ThenByDescending(x => x.TradePlan.ProfitPercent)
                .ThenByDescending(x => x.Score.Score)
                .Take(settings.MaxItems)
                .ToList();

            if (deduped.Count > 0)
                _logger.Info($"Premarket momentum summary prepared. Count={deduped.Count}");

            return deduped;
        }

        private void AddOrReplaceWishListContext(
            Dictionary<string, WishListContext> results,
            WishListContext item)
        {
            if (results.TryGetValue(item.Stock.Ticker, out var existing))
            {
                var existingPriority = CalculateWishListContextPriority(existing);
                var itemPriority = CalculateWishListContextPriority(item);
                var existingHasDailyRows = existing.DailyCandles is { Count: >= 2 };
                var itemHasDailyRows = item.DailyCandles is { Count: >= 2 };
                var preserveExistingLossScan =
                    IsLossPreset(existing.Preset.ScanCode) &&
                    IsNonDirectionalLivePreset(item.Preset.ScanCode) &&
                    !IsGainPreset(item.Preset.ScanCode);
                var preserveExistingDailyRows = existingHasDailyRows && !itemHasDailyRows;

                if (!preserveExistingLossScan &&
                    !preserveExistingDailyRows &&
                    (itemPriority > existingPriority ||
                    (itemPriority == existingPriority &&
                     item.WishListItem.Score.Score > existing.WishListItem.Score.Score)))
                {
                    results[item.Stock.Ticker] = item;

                    _logger.Info(
                        $"Ticker {item.Stock.Ticker} replaced existing wish list item with higher priority. " +
                        $"Old preset: {existing.Preset.ScanCode}, new preset: {item.Preset.ScanCode}, " +
                        $"OldPriority={existingPriority}, NewPriority={itemPriority}");
                }
                else
                {
                    _logger.Info(
                        $"Ticker {item.Stock.Ticker} already exists in wish list. " +
                        $"Keeping existing item from preset {existing.Preset.ScanCode}. " +
                        $"ExistingPriority={existingPriority}, NewPriority={itemPriority}" +
                        (preserveExistingLossScan
                            ? ", Reason=loss preset is not replaced by non-directional live preset"
                            : preserveExistingDailyRows
                                ? ", Reason=existing context has reliable D1 rows"
                            : string.Empty));
                }
            }
            else
            {
                results[item.Stock.Ticker] = item;
                _logger.Info($"Ticker {item.Stock.Ticker} added to wish list. Preset: {item.Preset.ScanCode}");
            }
        }

        private int CalculateWishListContextPriority(WishListContext ctx)
        {
            var score = 0;
            var dailyDistance = ctx.Snapshot.Current.DailyMaSignedDistancePct;
            var weeklyDistance = ctx.Snapshot.Current.WeeklyMaSignedDistancePct ?? 0m;
            var h4Distance = ctx.Snapshot.Current.H4MaSignedDistancePct;
            var dailyMacd = ctx.Snapshot.Current.DailyMACDLineMinusSignal;
            var h4Macd = ctx.Snapshot.Current.MACDLineMinusSignal;

            if (dailyDistance >= 0m)
                score += 2;

            if (string.Equals(ctx.Preset.ScanCode, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase))
                score += 4;
            else if (string.Equals(ctx.Preset.ScanCode, "TOP_OPEN_PERC_GAIN", StringComparison.OrdinalIgnoreCase))
                score += 3;
            else if (string.Equals(ctx.Preset.ScanCode, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase))
                score += 3;
            else if (string.Equals(ctx.Preset.ScanCode, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase))
                score += 3;

            if (dailyDistance > 15m)
                score += 2;
            if (weeklyDistance > 10m)
                score++;
            if (h4Distance > 5m)
                score++;

            if (dailyMacd > 0m)
                score += 2;
            if (h4Macd >= 0m)
                score++;

            if ((ctx.Snapshot.Current.WeeklyMACDLineMinusSignal ?? 0m) >= 0m)
                score++;

            return score;
        }

        private void AddOrReplaceHigherScore(
            Dictionary<string, CandidateDetails> results,
            CandidateDetails item,
            string bucketName)
        {
            var operationKey = BuildCandidateOperationKey(item);

            if (results.TryGetValue(operationKey, out var existing))
            {
                if (item.Score.Score > existing.Score.Score)
                {
                    results[operationKey] = item;

                    _logger.Info(
                        $"Ticker {item.Ticker} replaced existing {bucketName} operation with higher score. " +
                        $"Old preset: {existing.Scan.PresetScanCode}, new preset: {item.Scan.PresetScanCode}");
                }
                else
                {
                    _logger.Info(
                        $"Ticker {item.Ticker} already exists in {bucketName} with the same trade plan. " +
                        $"Keeping existing item from preset {existing.Scan.PresetScanCode}");
                }
            }
            else
            {
                results[operationKey] = item;
                _logger.Info($"Ticker {item.Ticker} added to {bucketName}. Preset: {item.Scan.PresetScanCode}");
            }
        }

        private static string BuildCandidateOperationKey(CandidateDetails item)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{item.Ticker}|{item.Scan.PresetScanCode}|{item.TradePlan.EntryPrice:G29}|{item.TradePlan.ExitPrice:G29}|{item.TradePlan.StopLoss:G29}");
        }

        private async Task TryAddCandidate(
            Dictionary<string, CandidateDetails> candidateResults,
            WishListItem mergedWishItem,
            WishListContext ctx,
            bool isFromWishlist,
            string marketTimezone,
            string bucketName,
            string rejectionLogPrefix)
        {
            var dailyFamilySplit = ClassifyDailyFamily(ctx, log: true);
            var diagnostics = BuildDiagnostics(ctx.Snapshot, ctx.Candles);
            var needsDeeperEntry = ResolveNeedsDeeperEntry(ctx.Snapshot, diagnostics);
            var entryScore = _candidateScore.Calculate(ctx.Snapshot);
            var recentSeries = BuildRecentFeatureSeries(ctx.Candles);
            var bbState = BuildBollingerStateSet(recentSeries);
            void EmitOtherCandidate(string reason, string shortReason)
            {
                // Keep rejected setups for evaluation without constructing an executable plan.
                var trade = new TradePlanInfo { LiveReferencePrice = ResolveScanPrice(ctx.Snapshot) };
                var dailyScore = mergedWishItem.Score.DailyScore ?? 0m;
                var weeklyScore = mergedWishItem.Score.WeeklyScore ?? 0m;
                var finalScore = dailyScore + weeklyScore + entryScore;
                var todayResearchLikePatternKind = ClassifyTodayResearchLikePatternKind(bbState, recentSeries);
                var todayResearchLikeSeriesScore = CalculateTodayResearchLikeSeriesScore(bbState, recentSeries);
                var needsMomentumExit = ResolveNeedsMomentumExit(ctx.Snapshot, diagnostics, entryScore);

                var candidateItem = BuildCandidateItem(
                    ctx.Stock,
                    isFromWishlist,
                    dailyFamilySplit,
                    needsDeeperEntry,
                    needsMomentumExit,
                    ctx.Preset,
                    ctx.Snapshot,
                    ctx.Candles,
                    trade,
                    diagnostics,
                    ctx.ScanTimeMarket,
                    marketTimezone,
                    dailyScore,
                    weeklyScore,
                    entryScore,
                    finalScore,
                    todayResearchLikePatternKind,
                    todayResearchLikeSeriesScore);

                candidateItem.CandidateSource = "Other";
                if (candidateItem.PatternVerdictReason.StartsWith("BellUp confirmed on ", StringComparison.OrdinalIgnoreCase))
                    candidateItem.PatternVerdictReason += $"; {shortReason}";
                candidateItem.Context.Notes = AppendDiagnosticNote(candidateItem.Context.Notes, reason);

                AddOrReplaceHigherScore(candidateResults, candidateItem, bucketName);
            }

            if (dailyFamilySplit == DailyFamilySplit.Unknown)
            {
                _logger.Info(
                    $"{rejectionLogPrefix}: {ctx.Stock.Ticker}. " +
                    "Daily family split is unknown.");
                EmitOtherCandidate(
                    "Rejected: daily family split is unknown", "Missing data");
                return;
            }

            var eligibilityReason = string.Empty;
            var isTodayResearchLikeCandidate =
                dailyFamilySplit == DailyFamilySplit.TodayResearchLike &&
                IsTodayResearchLikeCandidate(
                    mergedWishItem,
                    ctx,
                    diagnostics,
                    entryScore,
                    recentSeries,
                    bbState,
                    dailyFamilySplit,
                    out eligibilityReason);

            var bellPatternSignal = ClassifyBellPatternSignal(bbState, recentSeries);
            if (isTodayResearchLikeCandidate &&
                IsH4BellUpExhaustedWithFlatDaily(bellPatternSignal, ctx, recentSeries))
            {
                const string reason = "Rejected: H4 BellUp impulse exhausted while Daily Bollinger mid is flat";
                _logger.Info($"{rejectionLogPrefix}: {ctx.Stock.Ticker}. {reason}");
                EmitOtherCandidate(reason, "Exhausted");
                return;
            }

            if (dailyFamilySplit == DailyFamilySplit.TodayResearchLike && !isTodayResearchLikeCandidate)
            {
                _logger.Info(
                    $"{rejectionLogPrefix}: {ctx.Stock.Ticker}. " +
                    "Runaway candidate rejected because BellUp eligibility was not confirmed on H4/Daily.");
                EmitOtherCandidate(
                    $"Rejected: Runaway BellUp eligibility was not confirmed on H4/Daily. {eligibilityReason}", eligibilityReason);
                return;
            }

            if (isTodayResearchLikeCandidate &&
                !IsBellUpPhaseReadyToday(ctx, bellPatternSignal, out var burstReason, out var phaseReason))
            {
                _logger.Info($"{rejectionLogPrefix}: {ctx.Stock.Ticker}. {burstReason}");
                EmitOtherCandidate($"Rejected: {burstReason}", phaseReason);
                return;
            }

            if (dailyFamilySplit == DailyFamilySplit.Reversal)
            {
                var reversalPatternSeries = BuildReversalPatternSeries(ctx.DailyCandles);
                var reversalPatternSource = "D1";
                if (!HasMinimumReversalPatternRows(reversalPatternSeries))
                {
                    reversalPatternSeries = BuildH4ReversalPatternSeries(
                        ctx.Candles,
                        recentSeries);
                    reversalPatternSource = "H4 IPO fallback";
                }

                if (!HasMinimumReversalPatternRows(reversalPatternSeries))
                {
                    _logger.Info(
                        $"{rejectionLogPrefix}: {ctx.Stock.Ticker}. " +
                        $"ReversalHook not confirmed. " +
                        "Reason=reliable D1 and H4 pattern rows unavailable");
                    EmitOtherCandidate(
                        "Rejected: ReversalHook reliable D1 and H4 pattern rows unavailable", "Missing data");
                    return;
                }

                if (!IsReversalHookPattern(reversalPatternSeries!, out var reversalHookDiagnostics))
                {
                    _logger.Info(
                        $"{rejectionLogPrefix}: {ctx.Stock.Ticker}. " +
                        $"ReversalHook not confirmed on {reversalPatternSource} rows. " +
                        $"{reversalHookDiagnostics}");
                    EmitOtherCandidate(
                        $"Rejected: ReversalHook not confirmed on {reversalPatternSource} rows. {reversalHookDiagnostics}", "Reversal unconfirmed");
                    return;
                }

                if (reversalPatternSource == "D1" &&
                    IsH4ContradictingDailyReversalHook(bbState, recentSeries, out var h4ReversalDiagnostics))
                {
                    _logger.Info(
                        $"{rejectionLogPrefix}: {ctx.Stock.Ticker}. " +
                        $"ReversalHook not trade-ready. {h4ReversalDiagnostics}");
                    EmitOtherCandidate(
                        $"Rejected: ReversalHook not trade-ready. {h4ReversalDiagnostics}", "H4 contradiction");
                    return;
                }

                _logger.Info(
                    $"ReversalHook confirmed for {ctx.Stock.Ticker} on {reversalPatternSource}.");
            }

            var trade = ctx.Trade ??= await BuildTradePlan(ctx);
            if (isTodayResearchLikeCandidate &&
                IsLiveRunawayStructureInvalidated(
                    trade.LiveReferencePrice,
                    ctx.Candles,
                    recentSeries,
                    out var liveInvalidationReason))
            {
                _logger.Info(
                    $"{rejectionLogPrefix}: {ctx.Stock.Ticker}. " +
                    $"Runaway candidate rejected because the live price invalidated the saved H4 structure. " +
                    liveInvalidationReason);
                EmitOtherCandidate(
                    $"Rejected: Runaway live price invalidated saved H4 structure. {liveInvalidationReason}", "Structure broken");
                return;
            }

            var dailyScore = mergedWishItem.Score.DailyScore ?? 0m;
            var weeklyScore = mergedWishItem.Score.WeeklyScore ?? 0m;
            var finalScore = dailyScore + weeklyScore + entryScore;
            var todayResearchLikePatternKind = ClassifyTodayResearchLikePatternKind(bbState, recentSeries);
            var todayResearchLikeSeriesScore = CalculateTodayResearchLikeSeriesScore(bbState, recentSeries);

            var needsMomentumExit = ResolveNeedsMomentumExit(ctx.Snapshot, diagnostics, entryScore);

            var candidateItem = BuildCandidateItem(
                ctx.Stock,
                isFromWishlist,
                dailyFamilySplit,
                needsDeeperEntry,
                needsMomentumExit,
                ctx.Preset,
                ctx.Snapshot,
                ctx.Candles,
                trade,
                diagnostics,
                ctx.ScanTimeMarket,
                marketTimezone,
                dailyScore,
                weeklyScore,
                entryScore,
                finalScore,
                todayResearchLikePatternKind,
                todayResearchLikeSeriesScore);
            candidateItem.CandidateSource = dailyFamilySplit == DailyFamilySplit.TodayResearchLike
                ? "SameDayContinuation"
                : "Primary";

            _logger.Info(
                $"BB regimes for {ctx.Stock.Ticker}: " +
                $"W={candidateItem.WeeklyBbRegime}/{candidateItem.WeeklyBbDirection} " +
                $"D={candidateItem.DailyBbRegime}/{candidateItem.DailyBbDirection} " +
                $"H4={candidateItem.H4BbRegime}/{candidateItem.H4BbDirection}");

            AddOrReplaceHigherScore(candidateResults, candidateItem, bucketName);
        }

        private static string AppendDiagnosticNote(string? notes, string diagnostic)
        {
            if (string.IsNullOrWhiteSpace(notes))
                return diagnostic;

            return $"{notes}; {diagnostic}";
        }

        private static int GetCandidateSourcePriority(CandidateDetails candidate)
        {
            if (CandidateGroups.IsOther(candidate))
                return 0;

            if (string.Equals(candidate.CandidateSource, "Primary", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.CandidateSource, "SameDayContinuation", StringComparison.OrdinalIgnoreCase))
                return 2;

            return 1;
        }

        private bool IsTodayResearchLikeCandidate(
            WishListItem mergedWishItem,
            WishListContext ctx,
            CandidateDiagnostics diagnostics,
            decimal entryScore,
            RecentFeatureSeries recentSeries,
            BollingerStateSet bbState,
            DailyFamilySplit dailyFamilySplit,
            out string shortReason)
        {
            shortReason = string.Empty;
            if (dailyFamilySplit == DailyFamilySplit.Unknown)
            {
                shortReason = "Missing data";
                _logger.Info(
                    $"TodayResearchLike rejected: {ctx.Stock.Ticker}. " +
                    $"Reason=reliable daily close/mid rows unavailable.");
                return false;
            }

            if (dailyFamilySplit == DailyFamilySplit.Reversal)
            {
                shortReason = "Below mid";
                _logger.Info(
                    $"TodayResearchLike rejected and rerouted to Reversal: {ctx.Stock.Ticker}. " +
                    $"Reason=previous closed daily close is below previous closed daily Bollinger mid.");
                return false;
            }

            var patternKind = ClassifyTodayResearchLikePatternKind(bbState, recentSeries);
            var bellPatternSignal = ClassifyBellPatternSignal(bbState, recentSeries);

            if (!IsRunawayBellUpPattern(bellPatternSignal))
            {
                shortReason = "Pattern unconfirmed";
                _logger.Info(
                    $"TodayResearchLike pattern not confirmed: {ctx.Stock.Ticker}. " +
                    $"RequiredPattern=BellUp, " +
                    $"AllowedTimeframes=H4/Daily, " +
                    $"DetectedPattern={bellPatternSignal.Kind}, " +
                    $"DetectedTimeframe={bellPatternSignal.Timeframe}, " +
                    $"CandidatePattern={patternKind}, " +
                    $"DailyUpperTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.DailyBbUpperBandSeries, 6))}, " +
                    $"DailyMidTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.DailyBbMidBandSeries, 6))}, " +
                    $"DailyLowerTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.DailyBbLowerBandSeries, 6))}, " +
                    $"H4UpperTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.H4BbUpperBandSeries, 4))}, " +
                    $"H4MidTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.H4BbMidBandSeries, 4))}, " +
                    $"H4LowerTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.H4BbLowerBandSeries, 4))}, " +
                    $"H4RsiTail={_fmt.Generic(CalculateTailSlope(recentSeries.H4RsiSeries, 4))}, " +
                    $"BellPattern={bellPatternSignal.Kind}, " +
                    $"BellTimeframe={bellPatternSignal.Timeframe}");
                return false;
            }

            // Daily timing is a cross-timeframe veto only when Daily itself
            // confirms BellUp. A standalone H4 BellUp remains eligible for
            // early entries such as SECZ, where Daily has not formed yet.
            var dailyKind = BellPatternClassifier.ClassifyBellPatternKindForTimeframe(
                recentSeries.DailyBbUpperBandSeries,
                recentSeries.DailyBbMidBandSeries,
                recentSeries.DailyBbLowerBandSeries,
                ResolveSeriesDirection(recentSeries.DailyBbMidBandSeries),
                BellPatternTimeframe.Daily);
            if (dailyKind == BellPatternKind.BellUp &&
                !BellUpEntryTiming.IsReady(
                    ctx.DailyCandles ?? BuildDailyBars(ctx.Candles),
                    ctx.Candles,
                    Timeframe.D1,
                    ctx.ScanTimeMarket,
                    out var dailyTimingReason,
                    out var dailyTimingShortReason))
            {
                shortReason = $"Daily {dailyTimingShortReason}";
                _logger.Info(
                    $"TodayResearchLike pattern rejected: {ctx.Stock.Ticker}. " +
                    $"Reason=Daily BellUp timing veto. {dailyTimingReason}, " +
                    $"SelectedBellTimeframe={bellPatternSignal.Timeframe}");
                return false;
            }

            if (IsLateBellUpPhase(bellPatternSignal, recentSeries, out var latePhaseReason))
            {
                shortReason = "Late phase";
                _logger.Info(
                    $"TodayResearchLike pattern rejected: {ctx.Stock.Ticker}. " +
                    $"Reason={latePhaseReason}, " +
                    $"BellTimeframe={bellPatternSignal.Timeframe}");
                return false;
            }

            if (IsH4BellUpTerminalPullback(
                    bellPatternSignal,
                    recentSeries,
                    ctx.Candles,
                    out var terminalPullbackReason))
            {
                shortReason = "Terminal pullback";
                _logger.Info(
                    $"TodayResearchLike pattern rejected: {ctx.Stock.Ticker}. " +
                    $"Reason={terminalPullbackReason}, " +
                    $"BellTimeframe={bellPatternSignal.Timeframe}");
                return false;
            }

            if (!IsBellUpPatternReadyNow(bellPatternSignal, bbState, recentSeries))
            {
                shortReason = "Not ready";
                _logger.Info(
                    $"TodayResearchLike pattern kept out of trade-ready list: {ctx.Stock.Ticker}. " +
                    $"Pattern={patternKind}, " +
                    $"BellTimeframe={bellPatternSignal.Timeframe}, " +
                    $"Reason=pattern is not ready now, " +
                    $"DailyUpperTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.DailyBbUpperBandSeries, 6))}, " +
                    $"DailyMidTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.DailyBbMidBandSeries, 6))}, " +
                    $"DailyLowerTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.DailyBbLowerBandSeries, 6))}, " +
                    $"H4UpperTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.H4BbUpperBandSeries, 4))}, " +
                    $"H4MidTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.H4BbMidBandSeries, 4))}, " +
                    $"H4LowerTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.H4BbLowerBandSeries, 4))}, " +
                    $"H4RsiTail={_fmt.Generic(CalculateTailSlope(recentSeries.H4RsiSeries, 4))}, " +
                    $"BellPattern={bellPatternSignal.Kind}, " +
                    $"BellTimeframe={bellPatternSignal.Timeframe}");
                return false;
            }

            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            var dailyUpperSlope = CalculateRelativeSlopePct(recentSeries.DailyBbUpperBandSeries);
            var dailyLowerSlope = CalculateRelativeSlopePct(recentSeries.DailyBbLowerBandSeries);
            var weeklyMidSlope = CalculateRelativeSlopePct(recentSeries.WeeklyBbMidBandSeries);
            var weeklyUpperSlope = CalculateRelativeSlopePct(recentSeries.WeeklyBbUpperBandSeries);
            var weeklyLowerSlope = CalculateRelativeSlopePct(recentSeries.WeeklyBbLowerBandSeries);
            var h4MidSlope = CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries);
            var h4UpperSlope = CalculateRelativeSlopePct(recentSeries.H4BbUpperBandSeries);
            var h4LowerSlope = CalculateRelativeSlopePct(recentSeries.H4BbLowerBandSeries);
            var runawaySeriesScore = CalculateTodayResearchLikeSeriesScore(bbState, recentSeries);

            _logger.Info(
                $"TodayResearchLike promotion applied: {ctx.Stock.Ticker}. " +
                $"Pattern={patternKind}, " +
                $"Preset={ctx.Preset.ScanCode}, " +
                $"W={bbState.Weekly.Regime}/{bbState.Weekly.Direction}, " +
                $"D={bbState.Daily.Regime}/{bbState.Daily.Direction}, " +
                $"H4={bbState.H4.Regime}/{bbState.H4.Direction}, " +
                $"DailyUpperTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.DailyBbUpperBandSeries, 6))}, " +
                $"DailyMidTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.DailyBbMidBandSeries, 6))}, " +
                $"DailyLowerTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.DailyBbLowerBandSeries, 6))}, " +
                $"H4UpperTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.H4BbUpperBandSeries, 4))}, " +
                $"H4MidTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.H4BbMidBandSeries, 4))}, " +
                $"H4LowerTail={_fmt.Generic(CalculateTailRelativeSlopePct(recentSeries.H4BbLowerBandSeries, 4))}, " +
                $"DailyMidSlope={_fmt.Generic(dailyMidSlope)}%, " +
                $"DailyUpperSlope={_fmt.Generic(dailyUpperSlope)}%, " +
                $"DailyLowerSlope={_fmt.Generic(dailyLowerSlope)}%, " +
                $"WeeklyMidSlope={_fmt.Generic(weeklyMidSlope)}%, " +
                $"WeeklyUpperSlope={_fmt.Generic(weeklyUpperSlope)}%, " +
                $"WeeklyLowerSlope={_fmt.Generic(weeklyLowerSlope)}%, " +
                $"H4MidSlope={_fmt.Generic(h4MidSlope)}%, " +
                $"H4UpperSlope={_fmt.Generic(h4UpperSlope)}%, " +
                $"H4LowerSlope={_fmt.Generic(h4LowerSlope)}%, " +
                $"BellPattern={bellPatternSignal.Kind}, " +
                $"BellTimeframe={bellPatternSignal.Timeframe}, " +
                $"RunawaySeriesScore={_fmt.Generic(runawaySeriesScore)}, " +
                $"EntryScore={_fmt.Generic(entryScore)}, " +
                $"ATRRatio={_fmt.Generic(diagnostics.ATRRatio)}");

            return true;
        }

        private static bool IsTodayResearchLikePreLaunchCandidate(
            WishListItem mergedWishItem,
            WishListContext ctx,
            CandidateDiagnostics diagnostics,
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            var current = ctx.Snapshot.Current;
            var dailyDistance = current.DailyMaSignedDistancePct;
            var weeklyDistance = current.WeeklyMaSignedDistancePct ?? 0m;
            var score = mergedWishItem.Score.Score;
            var expectedBars = mergedWishItem.ExpectedBarsToTarget;
            var preset = ctx.Preset.ScanCode;
            var isLiveMoverPreset =
                string.Equals(preset, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(preset, "TOP_OPEN_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(preset, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(preset, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase);
            var isDeepScanPreset =
                string.Equals(preset, "TOP_PERC_LOSE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(preset, "TOP_OPEN_PERC_LOSE", StringComparison.OrdinalIgnoreCase);

            if (!isLiveMoverPreset && !isDeepScanPreset)
                return false;

            if (dailyDistance < -55m || dailyDistance > 8m)
                return false;

            if (current.DistanceTo20dHigh > -8m)
                return false;

            if (current.DailyRSI14 < 35m || current.DailyRSI14 > 62m)
                return false;

            if (current.WeeklyMACDLineMinusSignal.HasValue &&
                current.WeeklyMACDLineMinusSignal.Value > 0.8m)
            {
                return false;
            }

            var weeklyRunawayUp =
                string.Equals(bbState.Weekly.Direction, nameof(BollingerFigureDirection.Up), StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(bbState.Weekly.Regime, nameof(BollingerFigureRegime.Runaway), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(bbState.Weekly.Regime, nameof(BollingerFigureRegime.Pullback), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(bbState.Weekly.Regime, nameof(BollingerFigureRegime.Reacceleration), StringComparison.OrdinalIgnoreCase));

            var dailyTryingToTurn =
                ctx.Snapshot.DailyRsiDelta3 >= -5m &&
                recentSeries.DailyMacdHistogramSeries.LastOrDefault() > -0.75m;

            var h4MidSlope = CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries);
            var h4UpperSlope = CalculateRelativeSlopePct(recentSeries.H4BbUpperBandSeries);
            var h4NotBreakingDown =
                h4MidSlope > -10m &&
                h4UpperSlope > -10m &&
                recentSeries.H4MacdHistogramSeries.LastOrDefault() > -0.65m;

            var scoreQualified =
                score >= 14m ||
                (score >= 5m && expectedBars == 0) ||
                (isLiveMoverPreset && score >= 10m);

            return scoreQualified &&
                   dailyTryingToTurn &&
                   h4NotBreakingDown &&
                   (weeklyRunawayUp || weeklyDistance > -30m || diagnostics.ATRRatio >= 2.5m);
        }

        private static bool IsMixedMeanLiveWinnerCandidate(
            WishListContext ctx,
            CandidateDiagnostics diagnostics,
            RecentFeatureSeries recentSeries,
            decimal entryScore)
        {
            if (!IsLiveMoverPreset(ctx.Preset.ScanCode))
                return false;

            var rank = ctx.Stock.Rank > 0 ? ctx.Stock.Rank : int.MaxValue;
            if (rank > 10)
                return false;

            var current = ctx.Snapshot.Current;
            var weeklyDistance = current.WeeklyMaSignedDistancePct ?? 0m;
            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            var h4MidSlope = CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries);

            return weeklyDistance > 0m &&
                   current.DailyMaSignedDistancePct >= -8m &&
                   current.H4MaSignedDistancePct >= -12m &&
                   (dailyMidSlope >= 1.5m || h4MidSlope >= 2.5m) &&
                   (ctx.Snapshot.DailyMaDelta3 > 0m ||
                    ctx.Snapshot.H4MaDelta3 > 0m ||
                    entryScore >= 20m) &&
                   (diagnostics.ATRRatio >= 3m ||
                    current.DailyRSI14 >= 50m ||
                    entryScore >= 20m);
        }

        private bool IsAiReferenceLiveRecoveryCandidate(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            List<Candle> candles,
            RecentFeatureSeries recentSeries,
            BollingerStateSet bbState)
        {
            if (IsBelowPreviousClosedDailyMid(candles))
            {
                return false;
            }

            if (snapshot.Current.DistanceTo20dHigh > -8m)
                return false;

            if (snapshot.Current.DailyRSI14 < 38m || snapshot.Current.DailyRSI14 > 58m)
                return false;

            if (diagnostics.ATRRatio < 2.4m)
                return false;

            if (diagnostics.TrendPosition > 2m || diagnostics.DailyTrendPosition > 2m)
                return false;

            if (!IsStrictTodayResearchLikeRunawayPattern(bbState, recentSeries))
                return false;

            var weeklyMacdHistDelta = diagnostics.WeeklyMACDHistDelta ?? 0m;
            var dailyRsiSlope = CalculateSlope(recentSeries.DailyRsiSeries);
            var h4RsiSlope = CalculateSlope(recentSeries.H4RsiSeries);
            var dailyMacdSlope = CalculateSlope(recentSeries.DailyMacdHistogramSeries);
            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdHistogramSeries);

            var weeklyConstructive =
                bbState.Weekly.Direction == nameof(BollingerFigureDirection.Up) &&
                bbState.Weekly.Regime is nameof(BollingerFigureRegime.Collapse) or
                                     nameof(BollingerFigureRegime.Neutral) or
                                     nameof(BollingerFigureRegime.Pullback);

            var dailyConstructive =
                bbState.Daily.Direction is nameof(BollingerFigureDirection.Down) or
                                        nameof(BollingerFigureDirection.Flat);

            var h4Constructive =
                bbState.H4.Direction == nameof(BollingerFigureDirection.Up) &&
                bbState.H4.Regime is nameof(BollingerFigureRegime.Runaway) or
                                   nameof(BollingerFigureRegime.Reacceleration);

            return weeklyConstructive &&
                   dailyConstructive &&
                   h4Constructive &&
                   weeklyMacdHistDelta >= 0m &&
                   h4RsiSlope >= 0m &&
                   h4MacdSlope >= -0.20m &&
                   dailyMacdSlope >= -0.20m &&
                   dailyRsiSlope <= 5m;
        }

        private static bool IsShortHistoryLiveMoverCandidate(
            WishListContext ctx,
            CandidateDiagnostics diagnostics,
            RecentFeatureSeries recentSeries)
        {
            var preset = ctx.Preset.ScanCode;
            var isLiveMoverPreset =
                string.Equals(preset, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(preset, "TOP_OPEN_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(preset, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(preset, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase);

            if (!isLiveMoverPreset)
                return false;

            var hasUsableWeeklyBb =
                recentSeries.WeeklyBbMidBandSeries.Count >= 3 &&
                recentSeries.WeeklyBbUpperBandSeries.Count >= 3 &&
                recentSeries.WeeklyBbLowerBandSeries.Count >= 3;

            if (hasUsableWeeklyBb)
                return false;

            var current = ctx.Snapshot.Current;
            var dailyDistance = current.DailyMaSignedDistancePct;
            var h4Distance = current.H4MaSignedDistancePct;
            var dailyMacd = current.DailyMACDLineMinusSignal;
            var h4Macd = current.MACDLineMinusSignal;
            var weeklyMacd = current.WeeklyMACDLineMinusSignal;

            if (dailyDistance < -35m || dailyDistance > 320m)
                return false;

            if (h4Distance < -35m)
                return false;

            if (current.DistanceTo20dHigh > -0.25m)
                return false;

            if (current.DailyRSI14 < 35m || current.DailyRSI14 > 92m)
                return false;

            if (weeklyMacd.HasValue && weeklyMacd.Value < -3.0m)
                return false;

            var dailyTurningOrExplosive =
                ctx.Snapshot.DailyRsiDelta3 >= 8m ||
                dailyMacd >= -0.20m ||
                diagnostics.ATRRatio >= 4m;

            var h4Confirming =
                CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries) >= -5m ||
                CalculateRelativeSlopePct(recentSeries.H4BbUpperBandSeries) >= -5m ||
                h4Macd >= -0.50m;

            var momentumConfirming =
                dailyMacd >= -0.75m &&
                h4Macd >= -0.65m &&
                diagnostics.ATRRatio >= 2.0m;

            return dailyTurningOrExplosive &&
                   h4Confirming &&
                   momentumConfirming;
        }

        private static bool IsStrongRunawayUp(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            BollingerStateSet bbState)
        {
            var weeklyDistance = snapshot.Current.WeeklyMaSignedDistancePct ?? 0m;
            var dailyDistance = snapshot.Current.DailyMaSignedDistancePct;
            var h4Distance = snapshot.Current.H4MaSignedDistancePct;
            var weeklyMacd = snapshot.Current.WeeklyMACDLineMinusSignal ?? 0m;
            var dailyMacd = snapshot.Current.DailyMACDLineMinusSignal;
            var h4Macd = snapshot.Current.MACDLineMinusSignal;
            var weeklyUp = string.Equals(
                bbState.Weekly.Direction,
                nameof(BollingerFigureDirection.Up),
                StringComparison.OrdinalIgnoreCase);
            var dailyUp = string.Equals(
                bbState.Daily.Direction,
                nameof(BollingerFigureDirection.Up),
                StringComparison.OrdinalIgnoreCase);
            var h4Up = string.Equals(
                bbState.H4.Direction,
                nameof(BollingerFigureDirection.Up),
                StringComparison.OrdinalIgnoreCase);

            var score = 0;

            if (weeklyDistance > 10m)
                score++;
            if (dailyDistance > 15m)
                score++;
            if (h4Distance > 8m)
                score++;

            if (weeklyMacd > -0.25m)
                score++;
            if (dailyMacd > 0m)
                score++;
            if (h4Macd > -0.05m)
                score++;

            if (weeklyUp)
                score++;
            if (dailyUp)
                score++;
            if (h4Up)
                score++;

            if (bbState.Daily.WidthSlope > -15m)
                score++;
            if (bbState.H4.WidthSlope > -20m)
                score++;

            if (diagnostics.VolumeRatio20 >= 0.05m)
                score++;

            return dailyUp &&
                   (weeklyUp || h4Up) &&
                   dailyDistance > 15m &&
                   dailyMacd > 0m &&
                   score >= 7;
        }

        private bool IsTodayResearchLikePatternReadyNow(
            TodayResearchLikePatternKind patternKind,
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            var bellPatternSignal = ClassifyBellPatternSignal(bbState, recentSeries);
            if (bellPatternSignal.Kind == BellPatternKind.BellUp)
                return IsBellUpPatternReadyNow(bellPatternSignal, bbState, recentSeries);

            return patternKind switch
            {
                TodayResearchLikePatternKind.BellUp => IsBellUpPatternReadyNow(bellPatternSignal, bbState, recentSeries),
                TodayResearchLikePatternKind.Runaway => IsStrictTodayResearchLikeRunawayPatternReadyNow(bbState, recentSeries),
                TodayResearchLikePatternKind.LaunchContinuation => IsLaunchContinuationTodayResearchLikePatternReadyNow(bbState, recentSeries),
                TodayResearchLikePatternKind.PullbackContinuation => IsPullbackContinuationTodayResearchLikePatternReadyNow(bbState, recentSeries),
                _ => false
            };
        }

        private static bool IsRunawayBellUpPattern(BellPatternSignal bellPatternSignal)
        {
            return bellPatternSignal.Kind == BellPatternKind.BellUp &&
                   bellPatternSignal.Timeframe is BellPatternTimeframe.H4 or BellPatternTimeframe.Daily;
        }

        private static BollingerFigureDirection ParseDirection(string direction)
            => Enum.Parse<BollingerFigureDirection>(direction);

        private static BellPatternSignal ClassifyBellPatternSignal(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            if (IsPostSpikeConsolidation(
                    recentSeries.H4OpenSeries,
                    recentSeries.H4CloseSeries,
                    recentSeries.H4BbUpperBandSeries,
                    recentSeries.H4BbMidBandSeries,
                    recentSeries.H4BbLowerBandSeries))
                return new BellPatternSignal(BellPatternKind.Triangle, BellPatternTimeframe.H4);

            if (IsPostSpikeConsolidation(
                    recentSeries.DailyOpenSeries,
                    recentSeries.DailyCloseSeries,
                    recentSeries.DailyBbUpperBandSeries,
                    recentSeries.DailyBbMidBandSeries,
                    recentSeries.DailyBbLowerBandSeries))
                return new BellPatternSignal(BellPatternKind.Triangle, BellPatternTimeframe.Daily);

            var dailyKind = BellPatternClassifier.ClassifyBellPatternKindForTimeframe(
                recentSeries.DailyBbUpperBandSeries,
                recentSeries.DailyBbMidBandSeries,
                recentSeries.DailyBbLowerBandSeries,
                ParseDirection(bbState.Daily.Direction),
                BellPatternTimeframe.Daily);
            var h4Kind = BellPatternClassifier.ClassifyBellPatternKindForTimeframe(
                recentSeries.H4BbUpperBandSeries,
                recentSeries.H4BbMidBandSeries,
                recentSeries.H4BbLowerBandSeries,
                ParseDirection(bbState.H4.Direction),
                BellPatternTimeframe.H4);

            if (dailyKind == BellPatternKind.BellUp &&
                h4Kind == BellPatternKind.BellUp &&
                IsVerticalSpikeExpansion(recentSeries, BellPatternTimeframe.H4) &&
                !IsVerticalSpikeExpansion(recentSeries, BellPatternTimeframe.Daily))
            {
                return new BellPatternSignal(BellPatternKind.BellUp, BellPatternTimeframe.Daily);
            }

            var bellUpSignal = BellPatternClassifier.SelectBellPatternSignal(
                dailyKind,
                h4Kind,
                BellPatternKind.BellUp);
            if (bellUpSignal.Kind != BellPatternKind.None)
                return bellUpSignal;

            var bellDownSignal = BellPatternClassifier.SelectBellPatternSignal(
                dailyKind,
                h4Kind,
                BellPatternKind.BellDown);
            return bellDownSignal;
        }

        private static bool IsPostSpikeConsolidation(
            List<decimal> opens,
            List<decimal> closes,
            List<decimal> upper,
            List<decimal> mid,
            List<decimal> lower)
            => TrianglePatternClassifier.IsPostSpikeConsolidation(opens, closes, upper, mid, lower);

        private static BellPatternKind ClassifyBellPatternKind(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
            => ClassifyBellPatternSignal(bbState, recentSeries).Kind;

        private static bool IsBellUpPatternReadyNow(
            BellPatternSignal bellPatternSignal,
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            if (bellPatternSignal.Kind != BellPatternKind.BellUp)
                return false;

            if (IsVerticalSpikeExpansion(recentSeries, bellPatternSignal.Timeframe))
                return false;

            return bellPatternSignal.Timeframe switch
            {
                BellPatternTimeframe.H4 => bbState.H4.Direction != nameof(BollingerFigureDirection.Down) &&
                                           bbState.H4.Regime != nameof(BollingerFigureRegime.Collapse),
                BellPatternTimeframe.Daily => bbState.Daily.Direction != nameof(BollingerFigureDirection.Down) &&
                                              bbState.Daily.Regime != nameof(BollingerFigureRegime.Collapse) &&
                                              !IsH4ContradictingDailyBellUp(bbState, recentSeries),
                _ => false
            };
        }

        private static bool IsH4ContradictingDailyBellUp(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            if (bbState.H4.Direction == nameof(BollingerFigureDirection.Down) ||
                bbState.H4.Regime == nameof(BollingerFigureRegime.Collapse))
                return true;

            if (BellPatternClassifier.ClassifyBellPatternKindForTimeframe(
                    recentSeries.H4BbUpperBandSeries,
                    recentSeries.H4BbMidBandSeries,
                    recentSeries.H4BbLowerBandSeries,
                    ParseDirection(bbState.H4.Direction),
                    BellPatternTimeframe.H4) == BellPatternKind.BellUp)
            {
                return false;
            }

            var h4RsiTail = CalculateTailSlope(recentSeries.H4RsiSeries, 4);
            var h4MacdHistogramTail = CalculateTailSlope(recentSeries.H4MacdHistogramSeries, 4);

            return h4RsiTail < -3m &&
                   h4MacdHistogramTail <= 0m;
        }

        private static bool IsH4ContradictingDailyReversalHook(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries,
            out string diagnostics)
        {
            var h4RsiTail = CalculateTailSlope(recentSeries.H4RsiSeries, 4);
            var h4MacdHistogramTail = CalculateTailSlope(recentSeries.H4MacdHistogramSeries, 4);

            diagnostics =
                $"Reason=H4 does not confirm daily ReversalHook, " +
                $"H4={bbState.H4.Regime}/{bbState.H4.Direction}, " +
                $"H4RsiTail={h4RsiTail}, " +
                $"H4MacdHistogramTail={h4MacdHistogramTail}";

            if (bbState.H4.Regime == nameof(BollingerFigureRegime.Collapse))
                return true;

            if (bbState.H4.Direction == nameof(BollingerFigureDirection.Down) &&
                h4RsiTail <= 0m)
                return true;

            return false;
        }

        private static bool IsVerticalSpikeExpansion(
            RecentFeatureSeries recentSeries,
            BellPatternTimeframe timeframe)
        {
            IReadOnlyList<decimal> upper = timeframe switch
            {
                BellPatternTimeframe.H4 => recentSeries.H4BbUpperBandSeries,
                BellPatternTimeframe.Daily => recentSeries.DailyBbUpperBandSeries,
                _ => []
            };
            IReadOnlyList<decimal> lower = timeframe switch
            {
                BellPatternTimeframe.H4 => recentSeries.H4BbLowerBandSeries,
                BellPatternTimeframe.Daily => recentSeries.DailyBbLowerBandSeries,
                _ => []
            };
            IReadOnlyList<decimal> rsi = timeframe switch
            {
                BellPatternTimeframe.H4 => recentSeries.H4RsiSeries,
                BellPatternTimeframe.Daily => recentSeries.DailyRsiSeries,
                _ => []
            };
            IReadOnlyList<decimal> macdHistogram = timeframe switch
            {
                BellPatternTimeframe.H4 => recentSeries.H4MacdHistogramSeries,
                BellPatternTimeframe.Daily => recentSeries.DailyMacdHistogramSeries,
                _ => []
            };

            return BellPatternClassifier.IsVerticalSpikeExpansion(upper, lower, rsi, macdHistogram);
        }

        private bool IsBellUpPhaseReadyToday(
            WishListContext ctx,
            BellPatternSignal pattern,
            out string reason)
            => IsBellUpPhaseReadyToday(ctx, pattern, out reason, out _);

        private bool IsBellUpPhaseReadyToday(
            WishListContext ctx,
            BellPatternSignal pattern,
            out string reason,
            out string shortReason)
        {
            shortReason = string.Empty;
            if (!IsRunawayBellUpPattern(pattern))
            {
                shortReason = "Pattern unconfirmed";
                reason = "BellUp entry timing requires a confirmed Daily or H4 pattern";
                return false;
            }

            if (!BellUpEntryTiming.IsReady(
                ctx.DailyCandles ?? BuildDailyBars(ctx.Candles),
                ctx.Candles,
                pattern.Timeframe == BellPatternTimeframe.Daily ? Timeframe.D1 : Timeframe.H4,
                ctx.ScanTimeMarket,
                out reason,
                out shortReason))
                return false;

            var history = BuildBellUpHistory(ctx, pattern);
            if (BellUpLowerBandTurn.TryFindConfirmedTurn(history.Lower, history.Confirmed, out var trough))
            {
                shortReason = "Lower-band turn";
                reason = $"{pattern.Timeframe}: BellUp lower band turned up after a decline; " +
                         $"trough={history.Lower[trough]} ({history.Candles[trough].Time:yyyy-MM-dd HH:mm:ss}), " +
                         $"previous={history.Lower[^2]} ({history.Candles[^2].Time:yyyy-MM-dd HH:mm:ss}), " +
                         $"latest={history.Lower[^1]} ({history.Candles[^1].Time:yyyy-MM-dd HH:mm:ss}); " +
                         "both completed points are above the trough; do not enter";
                return false;
            }

            return true;
        }

        private bool IsH4BellUpExhaustedWithFlatDaily(
            BellPatternSignal pattern,
            WishListContext ctx,
            RecentFeatureSeries recentSeries)
        {
            if (pattern.Kind != BellPatternKind.BellUp ||
                pattern.Timeframe != BellPatternTimeframe.H4)
                return false;

            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            if (Math.Abs(dailyMidSlope) > 1m)
                return false;

            var history = BuildBellUpHistory(ctx, pattern);
            if (BellUpLowerBandTurn.TryFindConfirmedTurn(
                    history.Lower,
                    history.Confirmed,
                    out _))
                return true;

            // In combination with a flat Daily mid line, the first completed
            // point above the H4 trough is sufficient to flag exhaustion.
            var start = history.Lower.Count - 1;
            while (start > 0 && history.Confirmed[start - 1])
                start--;

            if (history.Lower.Count - start < 3)
                return false;

            var trough = start;
            for (var i = start; i < history.Lower.Count; i++)
            {
                if (history.Lower[i] <= 0m)
                    return false;
                if (history.Lower[i] <= history.Lower[trough])
                    trough = i;
            }

            return trough < history.Lower.Count - 1 &&
                   trough > start &&
                   history.Lower[start] > history.Lower[trough] &&
                   history.Lower[^1] > history.Lower[trough];
        }

        private static bool IsLateBellUpPhase(
            BellPatternSignal bellPatternSignal,
            RecentFeatureSeries recentSeries,
            out string reason)
        {
            reason = string.Empty;
            if (bellPatternSignal.Timeframe != BellPatternTimeframe.Daily)
                return false;

            var previousDailyBellUp = BellPatternClassifier.ClassifyBellPatternKindForTimeframe(
                recentSeries.DailyBbUpperBandSeries.SkipLast(1).ToList(),
                recentSeries.DailyBbMidBandSeries.SkipLast(1).ToList(),
                recentSeries.DailyBbLowerBandSeries.SkipLast(1).ToList(),
                ResolveSeriesDirection(recentSeries.DailyBbMidBandSeries.SkipLast(1)),
                BellPatternTimeframe.Daily);

            // Validated 2026-08-11 against evaluation-dataset.csv (AUC=0.61 for Runaway Win/Loss,
            // n=285): a Daily RSI rollover from its own recent peak is a real signal on its own,
            // independent of the H4-based checks below and of whether H4 bands are still expanding.
            // The band-slope deceleration itself tested weak (AUC=0.54) - RSI is what carries this.
            var dailyRsi = recentSeries.DailyRsiSeries;
            if (dailyRsi.Count >= 2)
            {
                var recentDailyRsiPeak = dailyRsi.TakeLast(Math.Min(4, dailyRsi.Count)).Max();
                var dailyRsiRollingOver = dailyRsi[^1] < dailyRsi[^2];

                if (recentDailyRsiPeak >= 70m && dailyRsiRollingOver)
                {
                    reason = "Daily RSI has already rolled over from a recent peak";
                    return true;
                }
            }

            var h4Rsi = recentSeries.H4RsiSeries;
            var h4Histogram = recentSeries.H4MacdHistogramSeries;
            if (h4Rsi.Count < 2 || h4Histogram.Count < 2)
                return false;

            var h4RsiRollingOver = h4Rsi[^1] < h4Rsi[^2];
            var h4HistogramRollingOver = h4Histogram[^1] < h4Histogram[^2];
            var recentH4RsiPeak = h4Rsi.TakeLast(Math.Min(4, h4Rsi.Count)).Max();

            if (previousDailyBellUp != BellPatternKind.BellUp &&
                recentH4RsiPeak >= 70m &&
                h4RsiRollingOver &&
                h4HistogramRollingOver)
            {
                reason = "Daily BellUp appeared only after H4 momentum had already rolled over";
                return true;
            }

            if (previousDailyBellUp == BellPatternKind.BellUp &&
                h4Rsi[^1] >= 75m &&
                CalculateTerminalBandOpeningPct(
                    recentSeries.H4BbUpperBandSeries,
                    recentSeries.H4BbMidBandSeries,
                    recentSeries.H4BbLowerBandSeries,
                    2) >= 4m)
            {
                reason = "Daily BellUp is no longer new and H4 bands are already in terminal expansion";
                return true;
            }

            return false;
        }

        private static bool IsH4BellUpTerminalPullback(
            BellPatternSignal bellPatternSignal,
            RecentFeatureSeries recentSeries,
            List<Candle> candles,
            out string reason)
        {
            reason = string.Empty;
            if (bellPatternSignal.Timeframe != BellPatternTimeframe.H4)
                return false;

            var count = Math.Min(
                recentSeries.H4BbUpperBandSeries.Count,
                recentSeries.H4BbMidBandSeries.Count);
            if (count < 4 || candles.Count < 2 || recentSeries.H4RsiSeries.Count < 2)
                return false;

            var upper = recentSeries.H4BbUpperBandSeries[^1];
            var mid = recentSeries.H4BbMidBandSeries[^1];
            if (upper <= mid)
                return false;

            var latest = candles[^1];
            var previous = candles[^2];
            var latestRed = latest.Close < latest.Open;
            var previousNearUpper = previous.Close >= mid + (upper - mid) * 0.55m;
            var closeBackToMiddle = latest.Close <= mid + (upper - mid) * 0.5m;
            var bodyBackToMiddle =
                latestRed &&
                latest.Open > mid + (upper - mid) * 0.55m &&
                closeBackToMiddle;
            var rsiRolledOver = recentSeries.H4RsiSeries[^1] < recentSeries.H4RsiSeries[^2];
            var macdHistogramRolledOver =
                recentSeries.H4MacdHistogramSeries.Count >= 2 &&
                recentSeries.H4MacdHistogramSeries[^1] <= recentSeries.H4MacdHistogramSeries[^2];
            var upperBandBentDown =
                recentSeries.H4BbUpperBandSeries.Count >= 2 &&
                recentSeries.H4BbUpperBandSeries[^1] <= recentSeries.H4BbUpperBandSeries[^2];
            var lowerBandClosing =
                recentSeries.H4BbLowerBandSeries.Count >= 2 &&
                recentSeries.H4BbLowerBandSeries[^1] >= recentSeries.H4BbLowerBandSeries[^2];

            if (bodyBackToMiddle &&
                previousNearUpper &&
                rsiRolledOver &&
                (macdHistogramRolledOver || upperBandBentDown || lowerBandClosing))
            {
                reason =
                    "H4 BellUp has terminal pullback/rollover: latest candle closed back toward mid with momentum rollover";
                return true;
            }

            return false;
        }

        private static BollingerFigureDirection ResolveSeriesDirection(IEnumerable<decimal> midSeries)
        {
            var values = midSeries.ToList();
            if (values.Count < 2)
                return BollingerFigureDirection.Flat;

            if (values[^1] > values[0])
                return BollingerFigureDirection.Up;

            if (values[^1] < values[0])
                return BollingerFigureDirection.Down;

            return BollingerFigureDirection.Flat;
        }

        private static decimal CalculateTerminalBandOpeningPct(
            List<decimal> upper,
            List<decimal> mid,
            List<decimal> lower,
            int lookback)
        {
            var count = Math.Min(upper.Count, Math.Min(mid.Count, lower.Count));
            var offset = Math.Max(1, lookback);
            if (count <= offset)
                return 0m;

            var startIndex = count - offset - 1;
            var endIndex = count - 1;
            var baseMid = mid[startIndex];
            if (baseMid == 0m)
                return 0m;

            var startWidth = upper[startIndex] - lower[startIndex];
            var endWidth = upper[endIndex] - lower[endIndex];
            return decimal.Round(
                (endWidth - startWidth) / Math.Abs(baseMid) * 100m,
                2,
                MidpointRounding.AwayFromZero);
        }

        private static bool IsStrictTodayResearchLikeRunawayPatternReadyNow(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            return bbState.H4.Direction == nameof(BollingerFigureDirection.Up) &&
                   bbState.Daily.Direction != nameof(BollingerFigureDirection.Down) &&
                   bbState.Weekly.Direction != nameof(BollingerFigureDirection.Down) &&
                   bbState.H4.Regime != nameof(BollingerFigureRegime.Collapse) &&
                   bbState.Daily.Regime != nameof(BollingerFigureRegime.Collapse) &&
                   bbState.Weekly.Regime != nameof(BollingerFigureRegime.Collapse);
        }

        private static bool IsLaunchContinuationTodayResearchLikePatternReadyNow(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            return bbState.Daily.Direction != nameof(BollingerFigureDirection.Down) &&
                   bbState.H4.Direction != nameof(BollingerFigureDirection.Down) &&
                   bbState.Weekly.Direction != nameof(BollingerFigureDirection.Down) &&
                   bbState.Weekly.Regime != nameof(BollingerFigureRegime.Collapse) &&
                   bbState.Daily.Regime != nameof(BollingerFigureRegime.Collapse) &&
                   bbState.H4.Regime != nameof(BollingerFigureRegime.Collapse);
        }

        private static bool IsPullbackContinuationTodayResearchLikePatternReadyNow(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            return bbState.Daily.Direction != nameof(BollingerFigureDirection.Down) &&
                   bbState.H4.Direction != nameof(BollingerFigureDirection.Down) &&
                   bbState.Daily.Regime != nameof(BollingerFigureRegime.Collapse) &&
                   bbState.H4.Regime != nameof(BollingerFigureRegime.Collapse);
        }

        private static TodayResearchLikePatternKind ClassifyTodayResearchLikePatternKind(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            var bellPatternKind = ClassifyBellPatternKind(bbState, recentSeries);
            if (bellPatternKind == BellPatternKind.BellUp)
                return TodayResearchLikePatternKind.BellUp;

            if (IsStrictTodayResearchLikeRunawayPattern(bbState, recentSeries))
                return TodayResearchLikePatternKind.Runaway;

            if (IsLaunchContinuationTodayResearchLikePattern(bbState, recentSeries))
                return TodayResearchLikePatternKind.LaunchContinuation;

            if (IsPullbackContinuationTodayResearchLikePattern(bbState, recentSeries))
                return TodayResearchLikePatternKind.PullbackContinuation;

            return TodayResearchLikePatternKind.None;
        }

        private static bool IsLaunchContinuationTodayResearchLikePattern(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            return (bbState.Weekly.Direction == nameof(BollingerFigureDirection.Up) ||
                    bbState.Weekly.Direction == nameof(BollingerFigureDirection.Flat) ||
                    bbState.Weekly.Regime == nameof(BollingerFigureRegime.Reacceleration) ||
                    bbState.Weekly.Regime == nameof(BollingerFigureRegime.Pullback)) &&
                   (bbState.Daily.Direction == nameof(BollingerFigureDirection.Up) ||
                    bbState.Daily.Direction == nameof(BollingerFigureDirection.Flat)) &&
                   (bbState.H4.Direction == nameof(BollingerFigureDirection.Up) ||
                    bbState.H4.Direction == nameof(BollingerFigureDirection.Flat)) &&
                   bbState.Daily.Regime != nameof(BollingerFigureRegime.Collapse) &&
                   bbState.H4.Regime != nameof(BollingerFigureRegime.Collapse);
        }

        private static bool IsPullbackContinuationTodayResearchLikePattern(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            return (bbState.Daily.Direction == nameof(BollingerFigureDirection.Up) ||
                    bbState.Daily.Direction == nameof(BollingerFigureDirection.Flat)) &&
                   (bbState.H4.Direction != nameof(BollingerFigureDirection.Down)) &&
                   bbState.Daily.Regime != nameof(BollingerFigureRegime.Collapse) &&
                   bbState.H4.Regime != nameof(BollingerFigureRegime.Collapse);
        }

        private decimal CalculateTodayResearchLikeSeriesScore(BollingerStateSet bbState, RecentFeatureSeries recentSeries)
        {
            var patternKind = ClassifyTodayResearchLikePatternKind(bbState, recentSeries);
            if (patternKind == TodayResearchLikePatternKind.None)
                return 0m;

            var bellPatternSignal = ClassifyBellPatternSignal(bbState, recentSeries);

            var dailyUpperRecent = CalculateTailRelativeSlopePct(recentSeries.DailyBbUpperBandSeries, 3);
            var dailyUpperPrior = CalculateSegmentRelativeSlopePct(recentSeries.DailyBbUpperBandSeries, 3, 3);
            var dailyMidRecent = CalculateTailRelativeSlopePct(recentSeries.DailyBbMidBandSeries, 3);
            var dailyMidPrior = CalculateSegmentRelativeSlopePct(recentSeries.DailyBbMidBandSeries, 3, 3);
            var dailyLowerRecent = CalculateTailRelativeSlopePct(recentSeries.DailyBbLowerBandSeries, 3);
            var dailyLowerPrior = CalculateSegmentRelativeSlopePct(recentSeries.DailyBbLowerBandSeries, 3, 3);
            var weeklyUpperRecent = CalculateTailRelativeSlopePct(recentSeries.WeeklyBbUpperBandSeries, 4);
            var weeklyUpperPrior = CalculateSegmentRelativeSlopePct(recentSeries.WeeklyBbUpperBandSeries, 4, 4);
            var weeklyMidRecent = CalculateTailRelativeSlopePct(recentSeries.WeeklyBbMidBandSeries, 4);
            var weeklyMidPrior = CalculateSegmentRelativeSlopePct(recentSeries.WeeklyBbMidBandSeries, 4, 4);
            var weeklyLowerRecent = CalculateTailRelativeSlopePct(recentSeries.WeeklyBbLowerBandSeries, 4);
            var weeklyLowerPrior = CalculateSegmentRelativeSlopePct(recentSeries.WeeklyBbLowerBandSeries, 4, 4);
            var h4UpperRecent = CalculateTailRelativeSlopePct(recentSeries.H4BbUpperBandSeries, 3);
            var h4UpperPrior = CalculateSegmentRelativeSlopePct(recentSeries.H4BbUpperBandSeries, 3, 3);
            var h4MidRecent = CalculateTailRelativeSlopePct(recentSeries.H4BbMidBandSeries, 3);
            var h4MidPrior = CalculateSegmentRelativeSlopePct(recentSeries.H4BbMidBandSeries, 3, 3);
            var h4LowerRecent = CalculateTailRelativeSlopePct(recentSeries.H4BbLowerBandSeries, 3);
            var h4LowerPrior = CalculateSegmentRelativeSlopePct(recentSeries.H4BbLowerBandSeries, 3, 3);
            var dailyMacdLast = recentSeries.DailyMacdHistogramSeries.LastOrDefault();
            var weeklyMacdLast = recentSeries.WeeklyMacdHistogramSeries.LastOrDefault();
            var h4MacdLast = recentSeries.H4MacdHistogramSeries.LastOrDefault();
            var dailyMacdSlope = CalculateSlope(recentSeries.DailyMacdHistogramSeries);
            var weeklyMacdSlope = CalculateSlope(recentSeries.WeeklyMacdHistogramSeries);
            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdHistogramSeries);
            var dailyRsiSlope = CalculateSlope(recentSeries.DailyRsiSeries);
            var h4RsiSlope = CalculateSlope(recentSeries.H4RsiSeries);

            decimal score = 0m;

            if (patternKind == TodayResearchLikePatternKind.Runaway)
            {
                if (IsBullishBandKink(dailyUpperPrior, dailyUpperRecent))
                    score += 3m;
                if (IsBullishBandKink(dailyMidPrior, dailyMidRecent))
                    score += 2m;
                if (IsBullishBandKink(dailyLowerPrior, dailyLowerRecent))
                    score += 1m;

                if (IsBullishBandKink(h4UpperPrior, h4UpperRecent))
                    score += 2m;
                if (IsBullishBandKink(h4MidPrior, h4MidRecent))
                    score += 1m;
                if (IsBullishBandKink(h4LowerPrior, h4LowerRecent))
                    score += 0.5m;
            }
            else if (patternKind == TodayResearchLikePatternKind.LaunchContinuation)
            {
                if (IsRealBollingerLaunch(dailyMidRecent, dailyUpperRecent, dailyLowerRecent))
                    score += 3m;
                if (IsRealBollingerLaunch(h4MidRecent, h4UpperRecent, h4LowerRecent))
                    score += 2m;

                if (weeklyUpperRecent >= weeklyMidRecent && weeklyMidRecent >= weeklyLowerRecent)
                    score += 1m;
                if (dailyUpperRecent >= dailyMidRecent && dailyMidRecent >= dailyLowerRecent)
                    score += 1m;
                if (h4UpperRecent >= h4MidRecent && h4MidRecent >= h4LowerRecent)
                    score += 1m;

                if (dailyMacdLast >= dailyMacdSlope)
                    score += 1.25m;
                if (weeklyMacdLast >= weeklyMacdSlope)
                    score += 0.75m;
                if (h4MacdLast >= h4MacdSlope)
                    score += 1m;

                if (dailyMacdSlope > 0m)
                    score += 0.75m;
                if (weeklyMacdSlope > 0m)
                    score += 0.5m;
                if (h4MacdSlope > 0m)
                    score += 0.75m;

                if (dailyRsiSlope > 0m)
                    score += 0.75m;
                if (h4RsiSlope > 0m)
                    score += 0.75m;
            }
            else if (patternKind == TodayResearchLikePatternKind.BellUp)
            {
                if (bellPatternSignal.Timeframe == BellPatternTimeframe.H4)
                    score += 2.0m;
                else if (bellPatternSignal.Timeframe == BellPatternTimeframe.Daily)
                    score += 1.5m;
            }
            else
            {
                if (dailyUpperRecent >= 0m && dailyUpperPrior > -0.50m)
                    score += 2m;
                if (dailyMidRecent >= -0.25m && dailyMidPrior > -0.75m)
                    score += 1.5m;
                if (weeklyUpperRecent >= -0.25m && weeklyMidRecent >= -0.25m)
                    score += 1m;
                if (h4UpperRecent >= -0.50m)
                    score += 2m;
                if (h4RsiSlope >= -0.10m)
                    score += 1m;
                if (h4MacdSlope >= -0.10m)
                    score += 1m;
            }

            if (weeklyUpperRecent > 0m && weeklyMidRecent >= 0m)
                score += 1m;

            if (dailyUpperRecent > 0m && dailyMidRecent >= 0m)
                score += 1m;
            if (weeklyUpperRecent > 0m && weeklyMidRecent >= 0m)
                score += 1m;
            if (h4UpperRecent > 0m && h4MidRecent >= 0m)
                score += 1m;

            if (dailyMacdLast > 0m)
                score += 2m;
            if (weeklyMacdLast > 0m)
                score += 1m;
            if (h4MacdLast >= 0m)
                score += 1m;

            if (dailyMacdSlope > -0.2m)
                score += 1m;
            if (weeklyMacdSlope > -0.2m)
                score += 1m;
            if (h4MacdSlope > -0.15m)
                score += 1m;

            if (dailyRsiSlope > 0m)
                score += 1m;
            if (h4RsiSlope > 0m)
                score += 1m;

            return score;
        }

        private bool IsBelowPreviousClosedDailyMid(List<Candle>? candles)
        {
            if (candles == null || candles.Count < 2)
                return false;

            return BuildDailySplitDiagnostic(candles, "row-based").IsBelowMid;
        }

        private DailyFamilySplit ClassifyDailyFamily(
            WishListContext ctx,
            bool log)
        {
            if (ctx.DailyCandles != null && ctx.DailyCandles.Count >= 2)
            {
                var d1Diagnostic = BuildDailySplitDiagnostic(ctx.DailyCandles, "D1");
                if (IsFreshDailySplitDiagnostic(d1Diagnostic))
                {
                    LogDailySplitDiagnostic(ctx.Stock.Ticker, d1Diagnostic, log);
                    return d1Diagnostic.IsBelowMid
                        ? DailyFamilySplit.Reversal
                        : DailyFamilySplit.TodayResearchLike;
                }

                if (log)
                {
                    _logger.Info(
                        $"Daily split D1 rows are stale for {ctx.Stock.Ticker}. " +
                        $"LatestRawDailyBarDate={d1Diagnostic.LatestRawDailyBarDate:yyyy-MM-dd}, " +
                        $"ExpectedLatestClosedDailyDate={GetExpectedLatestClosedDailyDate(d1Diagnostic.MarketToday):yyyy-MM-dd}. " +
                        "Daily family split is unknown; H4 aggregation is not used for the D1 family boundary.");
                }

                return DailyFamilySplit.Unknown;
            }

            if (ctx.Candles.Count >= _getCandidatesSettingsProvider.Get().Finder.MinimumCandles)
            {
                var recentSeries = BuildRecentFeatureSeries(ctx.Candles);
                var h4Mid = recentSeries.H4BbMidBandSeries.LastOrDefault();
                var h4Close = ctx.Candles
                    .OrderBy(x => x.Time)
                    .Last()
                    .Close;

                if (h4Mid > 0m && h4Close > 0m)
                {
                    if (log)
                    {
                        _logger.Info(
                            $"Daily split IPO fallback: {ctx.Stock.Ticker}. " +
                            $"Source=H4, Close={_fmt.Price(h4Close)}, Mid={_fmt.Price(h4Mid)}, " +
                            $"BelowMid={h4Close < h4Mid}");
                    }

                    return h4Close < h4Mid
                        ? DailyFamilySplit.Reversal
                        : DailyFamilySplit.TodayResearchLike;
                }
            }

            if (TryBuildBuiltDailyRowsSplitDiagnostic(ctx.Candles, out var rowDiagnostic, out var rowRejectionReason))
            {
                if (log)
                {
                    _logger.Info(
                        $"Daily split D1 rows are unavailable for {ctx.Stock.Ticker}. " +
                        $"Built H4 daily rows were not used. " +
                        $"LatestRawDailyBarDate={rowDiagnostic.LatestRawDailyBarDate:yyyy-MM-dd}, " +
                        $"ExpectedLatestClosedDailyDate={GetExpectedLatestClosedDailyDate(rowDiagnostic.MarketToday):yyyy-MM-dd}. " +
                        "Daily family split is unknown.");
                }
            }
            else if (log && !string.IsNullOrWhiteSpace(rowRejectionReason))
            {
                _logger.Info(
                    $"Daily split built rows are incomplete for {ctx.Stock.Ticker}. " +
                    $"{rowRejectionReason} " +
                    "Daily family split is unknown.");
            }

            return DailyFamilySplit.Unknown;
        }

        private void LogDailySplitDiagnostic(
            string ticker,
            DailySplitDiagnostic diagnostic,
            bool log)
        {
            if (!log)
                return;

            _logger.Info(
                $"Daily split diagnostic: {ticker}. " +
                $"Source={diagnostic.Source}, " +
                $"DailyBars={diagnostic.DailyBarsCount}, " +
                $"CompletedDailyBars={diagnostic.CompletedDailyBarsCount}, " +
                $"LatestRawDailyBarDate={diagnostic.LatestRawDailyBarDate:yyyy-MM-dd}, " +
                $"MarketToday={diagnostic.MarketToday:yyyy-MM-dd}, " +
                $"TrimmedCurrentDay={diagnostic.TrimmedCurrentDay}, " +
                $"Close={_fmt.Price(diagnostic.LatestClosedDailyClose)}, " +
                $"Mid={_fmt.Price(diagnostic.PreviousClosedDailyMid)}, " +
                $"BelowMid={diagnostic.IsBelowMid}");
        }

        private bool TryBuildBuiltDailyRowsSplitDiagnostic(
            List<Candle> candles,
            out DailySplitDiagnostic diagnostic,
            out string rejectionReason)
        {
            diagnostic = default;
            rejectionReason = string.Empty;

            if (candles.Count < 2)
            {
                rejectionReason = "Not enough candles to build daily rows.";
                return false;
            }

            var latestDailyDate = candles
                .MaxBy(x => x.Time)
                ?.Time.Date;

            if (latestDailyDate == null)
            {
                rejectionReason = "Unable to determine the latest daily date.";
                return false;
            }

            var latestDailyDateBars = candles.Count(x => x.Time.Date == latestDailyDate.Value);
            if (latestDailyDateBars < 2)
            {
                rejectionReason =
                    $"Latest daily aggregation has only {latestDailyDateBars} bar(s) on {latestDailyDate:yyyy-MM-dd}.";
                return false;
            }

            diagnostic = BuildDailySplitDiagnostic(candles, "BuiltDailyRows");
            return true;
        }

        private bool IsFreshDailySplitDiagnostic(DailySplitDiagnostic diagnostic)
        {
            if (diagnostic.LatestRawDailyBarDate == DateTime.MinValue)
                return false;

            return diagnostic.LatestRawDailyBarDate.Date >=
                   GetExpectedLatestClosedDailyDate(diagnostic.MarketToday);
        }

        private DateTime GetExpectedLatestClosedDailyDate(DateTime marketToday)
        {
            if (_expectedLatestClosedDailyDate.HasValue)
                return _expectedLatestClosedDailyDate.Value;

            var expected = marketToday.Date.AddDays(-1);
            while (expected.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                expected = expected.AddDays(-1);

            return expected;
        }

        private static DateTime? ResolveExpectedLatestClosedDailyDate(
            IEnumerable<WishListContext> contexts,
            DateTime marketToday)
        {
            var latestDates = contexts
                .Where(x => x.DailyCandles != null)
                .Select(x => x.DailyCandles!
                    .Select(candle => candle.Time.Date)
                    .Where(date => date < marketToday.Date)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max())
                .Where(x => x != DateTime.MinValue)
                .ToList();

            return latestDates.Count > 0
                ? latestDates
                    .GroupBy(x => x)
                    .OrderByDescending(group => group.Count())
                    .ThenByDescending(group => group.Key)
                    .Select(group => group.Key)
                    .First()
                : null;
        }

        private DailySplitDiagnostic BuildDailySplitDiagnostic(List<Candle> candles, string source)
        {
            var dailyBars = BuildDailyBars(candles);
            if (dailyBars.Count < 2)
            {
                return new DailySplitDiagnostic(
                    Source: source,
                    DailyBarsCount: dailyBars.Count,
                    CompletedDailyBarsCount: 0,
                    LatestRawDailyBarDate: dailyBars.Count > 0 ? dailyBars[^1].Time.Date : DateTime.MinValue,
                    MarketToday: MarketTime.Now().Date,
                    TrimmedCurrentDay: false,
                    LatestClosedDailyClose: 0m,
                    PreviousClosedDailyMid: 0m,
                    IsBelowMid: false);
            }

            var marketToday = MarketTime.Now().Date;
            var trimmedCurrentDay = dailyBars[^1].Time.Date == marketToday;
            var completedDailyBars = trimmedCurrentDay
                ? dailyBars.Take(dailyBars.Count - 1).ToList()
                : dailyBars;

            if (completedDailyBars.Count < 2)
            {
                return new DailySplitDiagnostic(
                    Source: source,
                    DailyBarsCount: dailyBars.Count,
                    CompletedDailyBarsCount: completedDailyBars.Count,
                    LatestRawDailyBarDate: dailyBars[^1].Time.Date,
                    MarketToday: marketToday,
                    TrimmedCurrentDay: trimmedCurrentDay,
                    LatestClosedDailyClose: completedDailyBars.Count > 0 ? completedDailyBars[^1].Close : 0m,
                    PreviousClosedDailyMid: 0m,
                    IsBelowMid: false);
            }

            var completedDailyFeatures = _featureEngine.Calculate(completedDailyBars, completedDailyBars.Count);
            var latestClosedDailyClose = completedDailyBars[^1].Close;
            var previousClosedDailyMid = completedDailyFeatures.DailyBollingerMidBand;

            return new DailySplitDiagnostic(
                Source: source,
                DailyBarsCount: dailyBars.Count,
                CompletedDailyBarsCount: completedDailyBars.Count,
                LatestRawDailyBarDate: dailyBars[^1].Time.Date,
                MarketToday: marketToday,
                TrimmedCurrentDay: trimmedCurrentDay,
                LatestClosedDailyClose: latestClosedDailyClose,
                PreviousClosedDailyMid: previousClosedDailyMid,
                IsBelowMid: latestClosedDailyClose < previousClosedDailyMid);
        }

        private static bool IsBelowPreviousClosedDailyMid(RecentFeatureSeries recentSeries)
            => BellPatternClassifier.IsBelowPreviousClosedDailyMid(
                recentSeries.DailyCloseSeries,
                recentSeries.DailyBbMidBandSeries);

        private bool IsReversalRecoveryTradeProfile(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            List<Candle> candles,
            RecentFeatureSeries recentSeries,
            BollingerStateSet bbState,
            ReversalRecoveryExitSettings settings)
        {
            if (!settings.Enabled || !IsBelowPreviousClosedDailyMid(candles))
                return false;

            var h4MacdSeries = recentSeries.H4MacdHistogramSeries;
            var h4RsiSlope = CalculateSlope(recentSeries.H4RsiSeries);
            var h4MacdSlope = CalculateSlope(h4MacdSeries);

            return diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   diagnostics.DailyTrendPosition <= settings.MaxDailyTrendPosition &&
                   diagnostics.TrendPosition <= settings.MaxTrendPosition &&
                   h4RsiSlope >= settings.MinH4RsiSlope &&
                   h4MacdSlope >= settings.MinH4MacdSlope &&
                   bbState.H4.Direction != nameof(BollingerFigureDirection.Down);
        }

        private static bool IsReversalSeriesRecoveryTradeProfile(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            RecentFeatureSeries recentSeries,
            ReversalRecoveryExitSettings settings)
        {
            if (!settings.Enabled)
                return false;

            if (snapshot.Current.DistanceTo20dHigh > settings.SeriesMaxDistanceTo20dHigh ||
                snapshot.Current.DailyRSI14 > settings.SeriesMaxDailyRsi14 ||
                diagnostics.ATRRatio < settings.SeriesMinAtrRatio)
            {
                return false;
            }

            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            var dailyWidthTail = CalculateTailBandWidthDeltaPct(
                recentSeries.DailyBbUpperBandSeries,
                recentSeries.DailyBbMidBandSeries,
                recentSeries.DailyBbLowerBandSeries,
                4);
            var h4WidthTail = CalculateTailBandWidthDeltaPct(
                recentSeries.H4BbUpperBandSeries,
                recentSeries.H4BbMidBandSeries,
                recentSeries.H4BbLowerBandSeries,
                4);
            var h4MacdTail = CalculateTailSlope(recentSeries.H4MacdHistogramSeries, 4);
            var h4RsiTail = CalculateTailSlope(recentSeries.H4RsiSeries, 4);
            var h4MacdLeg =
                h4MacdTail >= settings.SeriesMinH4MacdTailSlope &&
                h4RsiTail >= settings.SeriesMinH4RsiTailForMacdLeg;

            return dailyMidSlope <= settings.SeriesMaxDailyMidSlopePct &&
                   dailyWidthTail <= -settings.SeriesMinDailyWidthCompressionPct &&
                   h4RsiTail <= settings.SeriesMaxH4RsiTailSlope &&
                   (h4MacdLeg ||
                   h4WidthTail >= settings.SeriesMinH4WidthExpansionPct ||
                    h4RsiTail >= settings.SeriesMinH4RsiTailSlope);
        }

        private static bool IsReversalNearTermEntryTradeProfile(
            string presetScanCode,
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            RecentFeatureSeries recentSeries,
            ReversalRecoveryExitSettings settings)
        {
            if (!settings.Enabled)
                return false;

            if (IsGainPreset(presetScanCode))
                return false;

            if (diagnostics.ATRRatio < settings.NearTermMinAtrRatio ||
                snapshot.Current.DistanceTo20dHigh > settings.NearTermMaxDistanceTo20dHigh ||
                snapshot.Current.DailyRSI14 > settings.NearTermMaxDailyRsi14 ||
                diagnostics.BBMidSignedDistancePct > settings.NearTermMaxBbMid)
            {
                return false;
            }

            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            var dailyWidthTail = CalculateTailBandWidthDeltaPct(
                recentSeries.DailyBbUpperBandSeries,
                recentSeries.DailyBbMidBandSeries,
                recentSeries.DailyBbLowerBandSeries,
                4);
            var h4WidthTail = CalculateTailBandWidthDeltaPct(
                recentSeries.H4BbUpperBandSeries,
                recentSeries.H4BbMidBandSeries,
                recentSeries.H4BbLowerBandSeries,
                4);
            var h4RsiTail = CalculateTailSlope(recentSeries.H4RsiSeries, 4);
            var h4MacdTail = CalculateTailSlope(recentSeries.H4MacdHistogramSeries, 4);

            return dailyMidSlope >= settings.NearTermMinDailyMidSlopePct ||
                   dailyWidthTail <= -settings.NearTermMinDailyWidthCompressionPct ||
                   h4WidthTail >= settings.NearTermMinH4WidthExpansionPct ||
                   h4RsiTail >= settings.NearTermMinH4RsiTailSlope ||
                   h4MacdTail >= settings.NearTermMinH4MacdTailSlope;
        }

        private async Task<WishListContext?> TryBuildWishListContextFromExistingItem(
            WishListItem item,
            DateTime marketNow,
            string marketTimezone,
            FinderSettings finderSettings,
            TimeSpan contractResolveTimeout,
            int contractResolveMaxAttempts)
        {
            Contract? contract = null;
            List<Candle>? candles;

            if (!TryLoadPreparedH4CandlesFromCache(item.Ticker, finderSettings, out candles))
            {
                try
                {
                    contract = await _contractResolver.ResolveStockAsync(
                        item.Ticker,
                        contractResolveTimeout,
                        contractResolveMaxAttempts);

                    var end = MarketTime.Now();
                    var start = end.AddDays(-finderSettings.LookbackCalendarDays);

                    candles = await _historicalData.GetCandlesRange(
                        item.Ticker,
                        contract,
                        Timeframe.H4,
                        start,
                        end);

                    candles = PrepareFinderCandles(item.Ticker, candles, finderSettings);
                }
                catch (Exception ex)
                {
                    _logger.Info($"Skipping {item.Ticker}: failed to load wish list candles. {ex.Message}");
                    return null;
                }
            }

            if (candles == null || candles.Count < finderSettings.MinimumCandles)
            {
                _logger.Info(
                    $"Skipping {item.Ticker}: not enough wish list candles " +
                    $"({candles?.Count ?? 0} < {finderSettings.MinimumCandles}).");
                return null;
            }

            if (contract == null)
            {
                try
                {
                    contract = await _contractResolver.ResolveStockAsync(
                        item.Ticker,
                        TimeSpan.FromSeconds(Math.Max(15, finderSettings.ContractResolveTimeoutSeconds)),
                        Math.Max(1, finderSettings.ContractResolveMaxAttempts));
                }
                catch (Exception ex)
                {
                    _logger.Info($"Daily candles contract resolve skipped for {item.Ticker}. {ex.Message}");
                }
            }

            List<Candle>? dailyCandles = null;
            if (contract != null)
            {
                try
                {
                    var end = marketNow;
                    var start = end.AddDays(-finderSettings.LookbackCalendarDays);

                    dailyCandles = await _historicalData.GetCandlesRange(
                        item.Ticker,
                        contract,
                        Timeframe.D1,
                        start,
                        end);
                }
                catch (Exception ex)
                {
                    _logger.Info($"Daily candles load skipped for {item.Ticker}. {ex.Message}");
                }
            }

            var dailyBars = dailyCandles ?? BuildDailyBars(candles);
            if (TryRejectByRecentDailyPriceFloor(item.Ticker, dailyBars, out var recentPriceFloorReason))
            {
                _logger.Info($"Skipping {item.Ticker}: {recentPriceFloorReason}");
                return null;
            }

            CandidateSignalSnapshot snapshot;

            try
            {
                snapshot = _signalAnalyzer.Analyze(candles);
            }
            catch (Exception ex)
            {
                _logger.Info($"Skipping {item.Ticker}: failed to analyze wish list signals. {ex.Message}");
                return null;
            }

            var avgDollarVolume = CalculateAverageDollarVolumeDaily(candles, finderSettings.AvgVolumePeriod);
            var diagnostics = BuildDiagnostics(snapshot, candles);
            var entryScore = _candidateScore.Calculate(snapshot);
            var weeklyBbState = BuildBollingerStateSet(BuildRecentFeatureSeries(candles)).Weekly;

            var weeklyBbRejected = ShouldRejectByWeeklyBbForWishlist(weeklyBbState);
            var weeklyBbBypassed = weeklyBbRejected &&
                ShouldBypassAgedWeeklyBbVeto(item, snapshot, diagnostics, entryScore);

            if (weeklyBbRejected && !weeklyBbBypassed)
            {
                _logger.Info(
                    $"Skipping {item.Ticker}: weekly BB veto on aged wish list item. " +
                    $"Weekly={weeklyBbState.Regime}/{weeklyBbState.Direction}");
                return null;
            }

            if (weeklyBbBypassed)
            {
                _logger.Info(
                    $"Aged wish list weekly BB veto bypassed: {item.Ticker}. " +
                    $"Preset={item.Scan.PresetScanCode}, " +
                    $"Weekly={weeklyBbState.Regime}/{weeklyBbState.Direction}, " +
                    $"DailyDistance={_fmt.Generic(snapshot.Current.DailyMaSignedDistancePct)}%, " +
                    $"DistanceTo20dHigh={_fmt.Generic(snapshot.Current.DistanceTo20dHigh)}%, " +
                    $"ATRRatio={_fmt.Generic(diagnostics.ATRRatio)}, " +
                    $"DailyRsi14={_fmt.Generic(snapshot.Current.DailyRSI14)}, " +
                    $"EntryScore={_fmt.Generic(entryScore)}");
            }

            _logger.Info(
                $"Aged wish list context rebuilt: {item.Ticker}. " +
                $"H4={candles.Count}, AvgDollarVolume={avgDollarVolume}");

            return new WishListContext
            {
                Stock = new StockInfo
                {
                    Ticker = item.Ticker,
                    Exchange = contract?.Exchange ?? string.Empty,
                    Currency = contract?.Currency ?? string.Empty,
                    TradingClass = contract?.TradingClass ?? string.Empty,
                    ConId = contract?.ConId ?? 0
                },
                Contract = contract,
                Preset = new PresetScanCode(
                    item.Scan.PresetScanCode,
                    item.Scan.PresetDescription),
                Snapshot = snapshot,
                Candles = candles,
                DailyCandles = dailyCandles,
                ChartH4Candles = TryLoadSessionAlignedH4Candles(item.Ticker, marketNow),
                ScanTimeMarket = marketNow,
                AvgDollarVolumeDaily = avgDollarVolume,
                WishListItem = item
            };
        }

        private async Task<TradePlanInfo> BuildTradePlan(WishListContext ctx, ExecutionPriceSnapshot? publicationQuote = null)
        {
            var tradeSettings = _getCandidatesSettingsProvider.Get().TradePlan;
            var finderSettings = _getCandidatesSettingsProvider.Get().Finder;
            List<Candle>? entryCandles = ctx.EntryCandles;
            var entryObservedAt = ctx.EntryObservedAt;

            if (ctx.Contract == null)
            {
                try
                {
                    ctx.Contract = await _contractResolver.ResolveStockAsync(
                        ctx.Stock.Ticker,
                        TimeSpan.FromSeconds(Math.Max(15, finderSettings.ContractResolveTimeoutSeconds)),
                        Math.Max(1, finderSettings.ContractResolveMaxAttempts));
                }
                catch (Exception ex)
                {
                    _logger.Info($"Contract resolve skipped for trade plan {ctx.Stock.Ticker}. {ex.Message}");
                }
            }

            if (ctx.Contract != null && publicationQuote == null)
            {
                try
                {
                    var end = MarketTime.Now();
                    var start = ResolveEntryHistoryStart(end, tradeSettings.EntryLookbackHours);

                    ctx.PerformanceMetric?.AddHistoricalLoad();
                    entryCandles = await _historicalData.GetCandlesRange(
                        ctx.Stock.Ticker,
                        ctx.Contract,
                        Timeframe.M15,
                        start,
                        end);
                    entryObservedAt = MarketTime.Now();
                    ctx.EntryCandles = entryCandles;
                    ctx.EntryObservedAt = entryObservedAt;
                }
                catch (Exception ex)
                {
                    _logger.Info($"M15 entry history load failed for {ctx.Stock.Ticker}. {ex.Message}");
                }
            }

            var diagnostics = BuildDiagnostics(ctx.Snapshot, ctx.Candles);
            var entryScore = _candidateScore.Calculate(ctx.Snapshot);
            var needsDeeperEntry = ResolveNeedsDeeperEntry(ctx.Snapshot, diagnostics);
            var needsMomentumExit = ResolveNeedsMomentumExit(ctx.Snapshot, diagnostics, entryScore);
            var isConstructiveDeepMinFirst = IsConstructiveDeepMinFirstProxy(ctx.Snapshot, diagnostics, needsDeeperEntry);
            var entryDiscountOverridePct = ResolveDeepPullbackEntryDiscountPct(ctx.Snapshot, ctx.Candles, diagnostics, isConstructiveDeepMinFirst);
            var momentumExit = needsMomentumExit ? tradeSettings.MomentumExit : null;
            var isParabolicExpansion = IsParabolicExpansionProxy(ctx.Snapshot, diagnostics, needsDeeperEntry, needsMomentumExit);
            var isDeepParabolicExpansion = IsDeepParabolicExpansionProxy(ctx.Snapshot, diagnostics, needsDeeperEntry);
            var isExplosiveMinFirst = IsExplosiveMinFirstProxy(ctx.Snapshot, diagnostics, needsDeeperEntry, needsMomentumExit);
            var isExplosiveMaxFirst = IsExplosiveMaxFirstProxy(ctx.Snapshot, diagnostics);
            var recentSeries = BuildRecentFeatureSeries(ctx.Candles);
            var bbState = BuildBollingerStateSet(recentSeries);
            var todayResearchLikePatternKind = ClassifyTodayResearchLikePatternKind(bbState, recentSeries);
            var todayResearchLikeSeriesScore = CalculateTodayResearchLikeSeriesScore(bbState, recentSeries);
            var bellPatternSignal = ClassifyBellPatternSignal(bbState, recentSeries);
            var isBellUpPhaseReadyToday =
                ClassifyDailyFamily(ctx, log: false) == DailyFamilySplit.TodayResearchLike &&
                todayResearchLikePatternKind == TodayResearchLikePatternKind.BellUp &&
                IsBellUpPhaseReadyToday(ctx, bellPatternSignal, out _);
            if (publicationQuote != null && !isBellUpPhaseReadyToday)
                throw new InvalidOperationException("BellUp is no longer ready for a publication entry");
            var historicalPrice = ResolveScanPrice(ctx.Snapshot);
            var liveReferencePrice = publicationQuote?.Price ??
                ResolveLiveReferencePrice(entryCandles, historicalPrice);
            // Publication-only BellUp entry: current M5 open plus the immediately
            // preceding green M5 body. Fall back to the fresh current price when
            // the adjacent pair is unavailable. Other families keep their policy.
            var bellUpPublicationEntry = publicationQuote?.ProjectedEntryPrice ?? publicationQuote?.Price;
            var scanPrice = isBellUpPhaseReadyToday
                ? (bellUpPublicationEntry ?? liveReferencePrice)
                : historicalPrice;
            var scanPriceFloorOverride = ResolveSeriesBasedScanPriceFloor(
                scanPrice,
                recentSeries,
                bbState,
                todayResearchLikePatternKind);
            var isResearchLikeLaunch = IsResearchLikeLaunch(
                ctx.Snapshot,
                diagnostics,
                recentSeries,
                _nextDayRankingSettings);
            var isResearchLikeReadyNow = IsResearchLikeReadyNow(recentSeries, tradeSettings.ResearchLikeExit);
            var isReversalRecovery = IsReversalRecoveryTradeProfile(
                ctx.Snapshot,
                diagnostics,
                ctx.Candles,
                recentSeries,
                bbState,
                tradeSettings.ReversalRecoveryExit) ||
                IsReversalSeriesRecoveryTradeProfile(
                    ctx.Snapshot,
                    diagnostics,
                    recentSeries,
                    tradeSettings.ReversalRecoveryExit);
            var isReversalNearTermEntry = IsReversalNearTermEntryTradeProfile(
                ctx.Preset.ScanCode,
                ctx.Snapshot,
                diagnostics,
                recentSeries,
                tradeSettings.ReversalRecoveryExit);
            var aiReferenceLiveRecovery = IsAiReferenceLiveRecoveryCandidate(
                ctx.Snapshot,
                diagnostics,
                ctx.Candles,
                recentSeries,
                bbState);

            decimal? defaultProfitPctOverride = momentumExit?.DefaultProfitPct;
            decimal? minProfitPctOverride = momentumExit?.MinProfitPct;
            decimal? maxProfitPctOverride = momentumExit?.MaxProfitPct;
            decimal? maxLossPctOverride = null;

            if (isBellUpPhaseReadyToday)
            {
                var readyProfitProfile = ResolveAmplitudeAwareResearchLikeProfitProfile(
                    todayResearchLikePatternKind,
                    todayResearchLikeSeriesScore,
                    diagnostics.ATRRatio,
                    tradeSettings.ResearchLikeExit.DefaultProfitPct,
                    tradeSettings.ResearchLikeExit.MinProfitPct,
                    tradeSettings.ResearchLikeExit.MaxProfitPct);
                defaultProfitPctOverride = readyProfitProfile.DefaultProfitPct;
                minProfitPctOverride = readyProfitProfile.MinProfitPct;
                maxProfitPctOverride = readyProfitProfile.MaxProfitPct;

                _logger.Info(
                    $"Trade plan BellUp phase-ready profile applied for {ctx.Stock.Ticker}. " +
                    $"Confirmed BellUp passed entry timing; entering at reference price {scanPrice}. " +
                    $"DefaultProfitPct={_fmt.Percent(readyProfitProfile.DefaultProfitPct)}, " +
                    $"MinProfitPct={_fmt.Percent(readyProfitProfile.MinProfitPct)}, " +
                    $"MaxProfitPct={_fmt.Percent(readyProfitProfile.MaxProfitPct)}");
            }
            else if (isParabolicExpansion)
            {
                var parabolicSettings = tradeSettings.ParabolicExpansionExit;
                defaultProfitPctOverride = parabolicSettings.DefaultProfitPct;
                minProfitPctOverride = parabolicSettings.MinProfitPct;
                maxProfitPctOverride = parabolicSettings.MaxProfitPct;
                maxLossPctOverride = parabolicSettings.MaxLossPct;
                entryDiscountOverridePct = parabolicSettings.EntryDiscountPct;

                _logger.Info(
                    $"Trade plan parabolic expansion profile applied for {ctx.Stock.Ticker}. " +
                    $"EntryDiscountPct={_fmt.Percent(parabolicSettings.EntryDiscountPct)}, " +
                    $"DefaultProfitPct={_fmt.Percent(parabolicSettings.DefaultProfitPct)}, " +
                    $"MinProfitPct={_fmt.Percent(parabolicSettings.MinProfitPct)}, " +
                    $"MaxProfitPct={_fmt.Percent(parabolicSettings.MaxProfitPct)}, " +
                    $"MaxLossPct={_fmt.Percent(parabolicSettings.MaxLossPct)}");
            }
            else if (isDeepParabolicExpansion)
            {
                var deepParabolicSettings = tradeSettings.DeepParabolicExpansionExit;
                defaultProfitPctOverride = deepParabolicSettings.DefaultProfitPct;
                minProfitPctOverride = deepParabolicSettings.MinProfitPct;
                maxProfitPctOverride = deepParabolicSettings.MaxProfitPct;
                maxLossPctOverride = deepParabolicSettings.MaxLossPct;
                entryDiscountOverridePct = deepParabolicSettings.EntryDiscountPct;

                _logger.Info(
                    $"Trade plan deep parabolic profile applied for {ctx.Stock.Ticker}. " +
                    $"EntryDiscountPct={_fmt.Percent(deepParabolicSettings.EntryDiscountPct)}, " +
                    $"DefaultProfitPct={_fmt.Percent(deepParabolicSettings.DefaultProfitPct)}, " +
                    $"MinProfitPct={_fmt.Percent(deepParabolicSettings.MinProfitPct)}, " +
                    $"MaxProfitPct={_fmt.Percent(deepParabolicSettings.MaxProfitPct)}, " +
                    $"MaxLossPct={_fmt.Percent(deepParabolicSettings.MaxLossPct)}");
            }
            else if (isReversalRecovery)
            {
                var reversalRecoverySettings = tradeSettings.ReversalRecoveryExit;
                defaultProfitPctOverride = reversalRecoverySettings.DefaultProfitPct;
                minProfitPctOverride = reversalRecoverySettings.MinProfitPct;
                maxProfitPctOverride = reversalRecoverySettings.MaxProfitPct;
                maxLossPctOverride = reversalRecoverySettings.MaxLossPct;
                entryDiscountOverridePct = reversalRecoverySettings.EntryDiscountPct;

                _logger.Info(
                    $"Trade plan reversal-recovery profile applied for {ctx.Stock.Ticker}. " +
                    $"EntryDiscountPct={_fmt.Percent(reversalRecoverySettings.EntryDiscountPct)}, " +
                    $"DefaultProfitPct={_fmt.Percent(reversalRecoverySettings.DefaultProfitPct)}, " +
                    $"MinProfitPct={_fmt.Percent(reversalRecoverySettings.MinProfitPct)}, " +
                    $"MaxProfitPct={_fmt.Percent(reversalRecoverySettings.MaxProfitPct)}, " +
                    $"MaxLossPct={_fmt.Percent(reversalRecoverySettings.MaxLossPct)}");
            }
            else if (isReversalNearTermEntry)
            {
                var reversalRecoverySettings = tradeSettings.ReversalRecoveryExit;
                entryDiscountOverridePct = reversalRecoverySettings.NearTermEntryDiscountPct;

                _logger.Info(
                    $"Trade plan reversal near-term entry profile applied for {ctx.Stock.Ticker}. " +
                    $"EntryDiscountPct={_fmt.Percent(reversalRecoverySettings.NearTermEntryDiscountPct)}, " +
                    $"MaxEntryDiscountPct={_fmt.Percent(reversalRecoverySettings.NearTermMaxEntryDiscountPct)}");
            }
            else if (IsWeakDeepPullbackProxy(ctx.Snapshot, diagnostics, needsDeeperEntry))
            {
                var weakSettings = tradeSettings.WeakDeepPullbackExit;
                defaultProfitPctOverride = weakSettings.DefaultProfitPct;
                minProfitPctOverride = weakSettings.MinProfitPct;
                maxProfitPctOverride = weakSettings.MaxProfitPct;
                maxLossPctOverride = weakSettings.MaxLossPct;
                entryDiscountOverridePct = diagnostics.ATRRatio >= weakSettings.HighAtrRatioThreshold
                    ? weakSettings.HighAtrEntryDiscountPct
                    : weakSettings.EntryDiscountPct;

                _logger.Info(
                    $"Trade plan weak deep-pullback profile applied for {ctx.Stock.Ticker}. " +
                    $"EntryDiscountPct={_fmt.Percent(entryDiscountOverridePct ?? 0m)}, " +
                    $"DefaultProfitPct={_fmt.Percent(weakSettings.DefaultProfitPct)}, " +
                    $"MinProfitPct={_fmt.Percent(weakSettings.MinProfitPct)}, " +
                    $"MaxProfitPct={_fmt.Percent(weakSettings.MaxProfitPct)}, " +
                    $"MaxLossPct={_fmt.Percent(weakSettings.MaxLossPct)}");
            }
            else if (isConstructiveDeepMinFirst)
            {
                var constructiveSettings = tradeSettings.ConstructiveDeepMinFirst;
                defaultProfitPctOverride = constructiveSettings.DefaultProfitPct;
                minProfitPctOverride = constructiveSettings.MinProfitPct;
                maxProfitPctOverride = constructiveSettings.MaxProfitPct;

                _logger.Info(
                    $"Trade plan constructive deep MinFirst profile applied for {ctx.Stock.Ticker}. " +
                    $"EntryDiscountPct={_fmt.Percent(entryDiscountOverridePct ?? 0m)}, " +
                    $"DefaultProfitPct={_fmt.Percent(constructiveSettings.DefaultProfitPct)}, " +
                    $"MinProfitPct={_fmt.Percent(constructiveSettings.MinProfitPct)}, " +
                    $"MaxProfitPct={_fmt.Percent(constructiveSettings.MaxProfitPct)}");
            }
            else if (isExplosiveMinFirst)
            {
                var explosiveSettings = tradeSettings.ExplosiveMinFirstExit;
                defaultProfitPctOverride = explosiveSettings.DefaultProfitPct;
                minProfitPctOverride = explosiveSettings.MinProfitPct;
                maxProfitPctOverride = explosiveSettings.MaxProfitPct;
                entryDiscountOverridePct = explosiveSettings.EntryDiscountPct;

                _logger.Info(
                    $"Trade plan explosive MinFirst profile applied for {ctx.Stock.Ticker}. " +
                    $"EntryDiscountPct={_fmt.Percent(explosiveSettings.EntryDiscountPct)}, " +
                    $"DefaultProfitPct={_fmt.Percent(explosiveSettings.DefaultProfitPct)}, " +
                    $"MinProfitPct={_fmt.Percent(explosiveSettings.MinProfitPct)}, " +
                    $"MaxProfitPct={_fmt.Percent(explosiveSettings.MaxProfitPct)}");
            }
            else if (isExplosiveMaxFirst)
            {
                var explosiveMaxFirstSettings = tradeSettings.ExplosiveMaxFirstExit;
                defaultProfitPctOverride = explosiveMaxFirstSettings.DefaultProfitPct;
                minProfitPctOverride = explosiveMaxFirstSettings.MinProfitPct;
                maxProfitPctOverride = explosiveMaxFirstSettings.MaxProfitPct;
                maxLossPctOverride = explosiveMaxFirstSettings.MaxLossPct;

                _logger.Info(
                    $"Trade plan explosive MaxFirst profile applied for {ctx.Stock.Ticker}. " +
                    $"DefaultProfitPct={_fmt.Percent(explosiveMaxFirstSettings.DefaultProfitPct)}, " +
                    $"MinProfitPct={_fmt.Percent(explosiveMaxFirstSettings.MinProfitPct)}, " +
                    $"MaxProfitPct={_fmt.Percent(explosiveMaxFirstSettings.MaxProfitPct)}, " +
                    $"MaxLossPct={_fmt.Percent(explosiveMaxFirstSettings.MaxLossPct)}");
            }
            else if (IsStrongMinFirstProxy(ctx.Snapshot, diagnostics, needsDeeperEntry, needsMomentumExit))
            {
                var strongSettings = tradeSettings.StrongMinFirstExit;
                defaultProfitPctOverride = strongSettings.DefaultProfitPct;
                minProfitPctOverride = strongSettings.MinProfitPct;
                maxProfitPctOverride = strongSettings.MaxProfitPct;
                if (needsMomentumExit)
                    entryDiscountOverridePct = tradeSettings.MomentumExit.EntryDiscountPct;

                _logger.Info(
                    $"Trade plan strong MinFirst profile applied for {ctx.Stock.Ticker}. " +
                    $"DefaultProfitPct={_fmt.Percent(strongSettings.DefaultProfitPct)}, " +
                    $"MinProfitPct={_fmt.Percent(strongSettings.MinProfitPct)}, " +
                    $"MaxProfitPct={_fmt.Percent(strongSettings.MaxProfitPct)}");
            }
            else if (isResearchLikeLaunch && tradeSettings.ResearchLikeExit.Enabled)
            {
                var researchLikeSettings = tradeSettings.ResearchLikeExit;
                var researchLikeProfitProfile = ResolveAmplitudeAwareResearchLikeProfitProfile(
                    todayResearchLikePatternKind,
                    todayResearchLikeSeriesScore,
                    diagnostics.ATRRatio,
                    researchLikeSettings.DefaultProfitPct,
                    researchLikeSettings.MinProfitPct,
                    researchLikeSettings.MaxProfitPct);

                if (isResearchLikeReadyNow)
                {
                    defaultProfitPctOverride = researchLikeProfitProfile.DefaultProfitPct;
                    minProfitPctOverride = researchLikeProfitProfile.MinProfitPct;
                    maxProfitPctOverride = researchLikeProfitProfile.MaxProfitPct;
                    entryDiscountOverridePct ??= researchLikeSettings.EntryDiscountPct;

                    _logger.Info(
                        $"Trade plan research-like ready-now profile applied for {ctx.Stock.Ticker}. " +
                        $"EntryDiscountPct={_fmt.Percent(entryDiscountOverridePct ?? 0m)}, " +
                        $"Pattern={todayResearchLikePatternKind}, " +
                        $"SeriesScore={_fmt.Generic(todayResearchLikeSeriesScore)}, " +
                        $"DefaultProfitPct={_fmt.Percent(researchLikeProfitProfile.DefaultProfitPct)}, " +
                        $"MinProfitPct={_fmt.Percent(researchLikeProfitProfile.MinProfitPct)}, " +
                        $"MaxProfitPct={_fmt.Percent(researchLikeProfitProfile.MaxProfitPct)}");

                    if (aiReferenceLiveRecovery && tradeSettings.AiReferenceTradePlan.Enabled)
                    {
                        var aiSettings = tradeSettings.AiReferenceTradePlan;
                        var aiProfitProfile = ResolveAmplitudeAwareResearchLikeProfitProfile(
                            todayResearchLikePatternKind,
                            todayResearchLikeSeriesScore,
                            diagnostics.ATRRatio,
                            aiSettings.DefaultProfitPct,
                            aiSettings.MinProfitPct,
                            aiSettings.MaxProfitPct);

                        defaultProfitPctOverride = aiProfitProfile.DefaultProfitPct;
                        minProfitPctOverride = aiProfitProfile.MinProfitPct;
                        maxProfitPctOverride = aiProfitProfile.MaxProfitPct;
                        entryDiscountOverridePct ??= aiSettings.EntryDiscountPct;

                        _logger.Info(
                            $"Trade plan AI-reference profile applied for {ctx.Stock.Ticker}. " +
                            $"EntryDiscountPct={_fmt.Percent(entryDiscountOverridePct ?? 0m)}, " +
                            $"Pattern={todayResearchLikePatternKind}, " +
                            $"SeriesScore={_fmt.Generic(todayResearchLikeSeriesScore)}, " +
                            $"DefaultProfitPct={_fmt.Percent(aiProfitProfile.DefaultProfitPct)}, " +
                            $"MinProfitPct={_fmt.Percent(aiProfitProfile.MinProfitPct)}, " +
                            $"MaxProfitPct={_fmt.Percent(aiProfitProfile.MaxProfitPct)}");
                    }
                }
                else
                {
                    entryDiscountOverridePct ??= researchLikeSettings.EarlyEntryDiscountPct;

                    _logger.Info(
                        $"Trade plan research-like early-entry profile applied for {ctx.Stock.Ticker}. " +
                        $"EntryDiscountPct={_fmt.Percent(entryDiscountOverridePct ?? 0m)}");
                }
            }
            else if (aiReferenceLiveRecovery && tradeSettings.AiReferenceTradePlan.Enabled)
            {
                var aiSettings = tradeSettings.AiReferenceTradePlan;
                var aiProfitProfile = ResolveAmplitudeAwareResearchLikeProfitProfile(
                    todayResearchLikePatternKind,
                    todayResearchLikeSeriesScore,
                    diagnostics.ATRRatio,
                    aiSettings.DefaultProfitPct,
                    aiSettings.MinProfitPct,
                    aiSettings.MaxProfitPct);

                defaultProfitPctOverride = aiProfitProfile.DefaultProfitPct;
                minProfitPctOverride = aiProfitProfile.MinProfitPct;
                maxProfitPctOverride = aiProfitProfile.MaxProfitPct;
                entryDiscountOverridePct ??= aiSettings.EntryDiscountPct;

                _logger.Info(
                    $"Trade plan AI-reference profile applied for {ctx.Stock.Ticker}. " +
                    $"EntryDiscountPct={_fmt.Percent(entryDiscountOverridePct ?? 0m)}, " +
                    $"Pattern={todayResearchLikePatternKind}, " +
                    $"SeriesScore={_fmt.Generic(todayResearchLikeSeriesScore)}, " +
                    $"DefaultProfitPct={_fmt.Percent(aiProfitProfile.DefaultProfitPct)}, " +
                    $"MinProfitPct={_fmt.Percent(aiProfitProfile.MinProfitPct)}, " +
                    $"MaxProfitPct={_fmt.Percent(aiProfitProfile.MaxProfitPct)}");
            }
            else if (needsMomentumExit)
            {
                entryDiscountOverridePct = tradeSettings.MomentumExit.EntryDiscountPct;

                _logger.Info(
                    $"Trade plan momentum entry profile applied for {ctx.Stock.Ticker}. " +
                    $"EntryDiscountPct={_fmt.Percent(tradeSettings.MomentumExit.EntryDiscountPct)}");
            }

            var bbEntryDiscountOverridePct = ResolveBollingerEntryDiscountOverridePct(
                bbState,
                entryDiscountOverridePct);

            if (bbEntryDiscountOverridePct != entryDiscountOverridePct)
            {
                _logger.Info(
                    $"Trade plan H4 BB entry adjustment applied for {ctx.Stock.Ticker}. " +
                    $"W={bbState.Weekly.Regime}/{bbState.Weekly.Direction}, " +
                    $"D={bbState.Daily.Regime}/{bbState.Daily.Direction}, " +
                    $"H4={bbState.H4.Regime}/{bbState.H4.Direction}, " +
                    $"EntryDiscountPct={_fmt.Percent(bbEntryDiscountOverridePct ?? 0m)}, " +
                    $"H4MidSlope={_fmt.Percent(bbState.H4.MidSlope)}");
            }

            if (isExplosiveMinFirst &&
                tradeSettings.ExplosiveMinFirstExit.MaxEntryDiscountPct >= 0m)
            {
                var explosiveEntryDiscountOverridePct = CapDiscount(
                    bbEntryDiscountOverridePct,
                    tradeSettings.ExplosiveMinFirstExit.MaxEntryDiscountPct);

                if (explosiveEntryDiscountOverridePct != bbEntryDiscountOverridePct)
                {
                    _logger.Info(
                        $"Trade plan explosive MinFirst entry cap applied for {ctx.Stock.Ticker}. " +
                        $"EntryDiscountPct={_fmt.Percent(explosiveEntryDiscountOverridePct ?? 0m)}, " +
                        $"PreviousEntryDiscountPct={_fmt.Percent(bbEntryDiscountOverridePct ?? 0m)}");
                }

                bbEntryDiscountOverridePct = explosiveEntryDiscountOverridePct;
            }

            entryDiscountOverridePct = bbEntryDiscountOverridePct;

            var triangleEntryDiscountOverridePct = ResolveH4TriangleEntryDiscountOverridePct(
                ctx.Candles,
                entryCandles,
                tradeSettings.H4BollingerEntry.Triangle,
                entryDiscountOverridePct);

            if (triangleEntryDiscountOverridePct != entryDiscountOverridePct)
            {
                _logger.Info(
                    $"Trade plan H4 triangle entry adjustment applied for {ctx.Stock.Ticker}. " +
                    $"EntryDiscountPct={_fmt.Percent(triangleEntryDiscountOverridePct ?? 0m)}");
            }

            entryDiscountOverridePct = triangleEntryDiscountOverridePct;

            var seriesEntryProfile = ResolveSeriesEntryProfileDiscountOverridePct(
                ctx.Snapshot,
                diagnostics,
                recentSeries,
                bbState,
                tradeSettings.SeriesEntryProfile,
                entryDiscountOverridePct);

            if (seriesEntryProfile.EntryDiscountPct != entryDiscountOverridePct)
            {
                _logger.Info(
                    $"Trade plan series entry profile applied for {ctx.Stock.Ticker}. " +
                    $"Profile={seriesEntryProfile.Profile}, " +
                    $"EntryDiscountPct={_fmt.Percent(seriesEntryProfile.EntryDiscountPct ?? 0m)}, " +
                    $"DailyMidSlope={_fmt.Percent(seriesEntryProfile.DailyMidSlope / 100m)}, " +
                    $"DailyRsiSlope={seriesEntryProfile.DailyRsiSlope:0.##}, " +
                    $"H4MidSlope={_fmt.Percent(seriesEntryProfile.H4MidSlope / 100m)}, " +
                    $"H4RsiSlope={seriesEntryProfile.H4RsiSlope:0.##}, " +
                    $"H4MacdSlope={seriesEntryProfile.H4MacdSlope:0.####}, " +
                    $"ATRRatio={diagnostics.ATRRatio:0.##}, " +
                    $"D={bbState.Daily.Regime}/{bbState.Daily.Direction}, " +
                    $"H4={bbState.H4.Regime}/{bbState.H4.Direction}");
            }

            entryDiscountOverridePct = seriesEntryProfile.EntryDiscountPct;

            if ((isReversalRecovery || isReversalNearTermEntry) &&
                (isReversalRecovery
                    ? tradeSettings.ReversalRecoveryExit.MaxEntryDiscountPct
                    : tradeSettings.ReversalRecoveryExit.NearTermMaxEntryDiscountPct) >= 0m)
            {
                var maxRecoveryEntryDiscountPct = isReversalRecovery
                    ? tradeSettings.ReversalRecoveryExit.MaxEntryDiscountPct
                    : tradeSettings.ReversalRecoveryExit.NearTermMaxEntryDiscountPct;
                var cappedRecoveryEntryDiscountPct = CapDiscount(
                    entryDiscountOverridePct,
                    maxRecoveryEntryDiscountPct);

                if (cappedRecoveryEntryDiscountPct != entryDiscountOverridePct)
                {
                    _logger.Info(
                        $"Trade plan reversal entry cap applied for {ctx.Stock.Ticker}. " +
                        $"EntryDiscountPct={_fmt.Percent(cappedRecoveryEntryDiscountPct ?? 0m)}, " +
                        $"PreviousEntryDiscountPct={_fmt.Percent(entryDiscountOverridePct ?? 0m)}");
                }

                entryDiscountOverridePct = cappedRecoveryEntryDiscountPct;
            }

            var trade = _tradeBuilder.Build(
                ctx.Candles,
                entryCandles,
                scanPrice,
                scanPriceFloorOverride,
                entryDiscountOverridePct,
                ClassifyDailyFamily(ctx, log: false) == DailyFamilySplit.Reversal
                    ? TradeEntryPatternFamily.ReversalHook
                    : TradeEntryPatternFamily.BellUp,
                defaultProfitPctOverride,
                minProfitPctOverride,
                maxProfitPctOverride,
                maxLossPctOverride,
                isBellUpPhaseReadyToday);

            if (isBellUpPhaseReadyToday)
            {
                if (TryBuildBellUpBoostExit(ctx, bellPatternSignal, trade.Entry, out var boostTarget, out var boostReason))
                {
                    var earlierBoost = boostTarget.EarlierBoost;
                    var earlierBoostText = earlierBoost == null
                        ? "none"
                        : earlierBoost.Time.ToString("yyyy-MM-dd HH:mm:ss");
                    var earlierBodyText = earlierBoost == null
                        ? "none"
                        : (earlierBoost.Close - earlierBoost.Open).ToString();
                    _logger.Info(
                        $"BellUp {boostTarget.BoostCount}-boost exit applied for {ctx.Stock.Ticker}. Timeframe={bellPatternSignal.Timeframe}, " +
                        $"LatestBoost={boostTarget.LatestBoost.Time:yyyy-MM-dd HH:mm:ss}, " +
                        $"LatestBody={boostTarget.LatestBoost.Close - boostTarget.LatestBoost.Open}, " +
                        $"EarlierBoost={earlierBoostText}, EarlierBody={earlierBodyText}, " +
                        $"AverageBody={boostTarget.AverageBody}, PreviousExit={trade.Exit}, Exit={boostTarget.ExitPrice}. " +
                        "Entry and stop unchanged.");
                    trade.Exit = boostTarget.ExitPrice;
                    trade.ExitProfile = $"bellup-{boostTarget.BoostCount}-boost-{bellPatternSignal.Timeframe}";
                }
                else
                {
                    _logger.Info($"BellUp boost exit not applied for {ctx.Stock.Ticker}: {boostReason}. Existing exit retained.");
                }
            }

            var plan = new TradePlanInfo
            {
                LiveReferencePrice = liveReferencePrice,
                // Ordinary cached history has no receipt/version timestamp per bar.
                // Preserve its bar start without inventing an exact price-as-of time.
                ReferencePriceBarTime = entryCandles?.Count > 0 ? entryCandles.Max(x => x.Time) : null,
                ReferencePriceObservedAt = entryObservedAt,
                ReferencePriceSource = entryCandles?.Count > 0 ? "M15History" : "IndicatorFallback",
                EntryPriceSource = publicationQuote == null
                    ? (isBellUpPhaseReadyToday ? "BellUpFreshPrice" : "ExistingTradePlan")
                    : publicationQuote.ProjectedEntryPrice.HasValue
                        ? "BellUpM5BodyContinuation"
                        : "BellUpFreshM5PriceFallback",
                PlanBuiltAt = MarketTime.Now(),
                EntryPrice = trade.Entry,
                ExitPrice = trade.Exit,
                StopLoss = trade.Stop,
                StopLimitPrice = trade.StopLimit,
                ProfitPercent = CalculatePercent(trade.Entry, trade.Exit),
                LossPercent = CalculatePercent(trade.Entry, trade.Stop),
                ExitProfile = trade.ExitProfile
            };
            publicationQuote?.ApplyTo(plan);
            return plan;
        }

        private bool TryBuildBellUpBoostExit(
            WishListContext ctx,
            BellPatternSignal pattern,
            decimal entry,
            out BellUpBoostExitTarget target,
            out string reason)
        {
            var history = BuildBellUpHistory(ctx, pattern);
            return BellUpBoostExit.TryCalculate(history.Candles, history.Confirmed, entry, out target, out reason);
        }

        private (List<Candle> Candles, List<bool> Confirmed, List<decimal> Lower) BuildBellUpHistory(
            WishListContext ctx,
            BellPatternSignal pattern)
        {
            var isDaily = pattern.Timeframe == BellPatternTimeframe.Daily;
            var completed = BellUpEntryTiming.GetCompletedCandles(
                isDaily ? ctx.DailyCandles ?? BuildDailyBars(ctx.Candles) : ctx.Candles,
                isDaily ? Timeframe.D1 : Timeframe.H4,
                ctx.ScanTimeMarket);
            var upper = new List<decimal>();
            var mid = new List<decimal>();
            var lower = new List<decimal>();
            var confirmed = new List<bool>();
            var lowerHistory = new List<decimal>();
            var lookback = isDaily ? RecentDailySeriesLength : RecentH4SeriesLength;

            // Replay the shared classifier on prefixes; never infer an old phase from future bands.
            for (var i = 0; i < completed.Count; i++)
            {
                var features = _featureEngine.Calculate(completed, i + 1);
                upper.Add(decimal.Round(isDaily ? features.DailyBollingerUpperBand : features.H4BollingerUpperBand, 2, MidpointRounding.AwayFromZero));
                mid.Add(decimal.Round(isDaily ? features.DailyBollingerMidBand : features.H4BollingerMidBand, 2, MidpointRounding.AwayFromZero));
                lower.Add(decimal.Round(isDaily ? features.DailyBollingerLowerBand : features.H4BollingerLowerBand, 2, MidpointRounding.AwayFromZero));
                lowerHistory.Add(lower[^1]);
                if (upper.Count > lookback)
                {
                    upper.RemoveAt(0);
                    mid.RemoveAt(0);
                    lower.RemoveAt(0);
                }

                confirmed.Add(BellPatternClassifier.ClassifyBellPatternKindForTimeframe(
                    upper, mid, lower, ResolveSeriesDirection(mid), pattern.Timeframe) == BellPatternKind.BellUp);
            }

            return (completed, confirmed, lowerHistory);
        }

        private static decimal ResolveLiveReferencePrice(
            List<Candle>? entryCandles,
            decimal fallbackPrice)
        {
            if (entryCandles == null || entryCandles.Count == 0)
                return fallbackPrice;

            return entryCandles
                .OrderBy(x => x.Time)
                .Last()
                .Close;
        }

        private static bool IsLiveRunawayStructureInvalidated(
            decimal liveReferencePrice,
            List<Candle> h4Candles,
            RecentFeatureSeries recentSeries,
            out string reason)
        {
            reason = string.Empty;
            if (liveReferencePrice <= 0m ||
                h4Candles.Count == 0 ||
                recentSeries.H4BbMidBandSeries.Count == 0)
            {
                return false;
            }

            var orderedH4 = h4Candles
                .OrderBy(x => x.Time)
                .ToList();
            var latestH4Mid = recentSeries.H4BbMidBandSeries[^1];
            if (latestH4Mid <= 0m || liveReferencePrice >= latestH4Mid)
            {
                return false;
            }

            var comparableCount = Math.Min(orderedH4.Count, recentSeries.H4BbMidBandSeries.Count);
            var lookbackCount = Math.Min(2, comparableCount);
            decimal? priorCloseAboveMid = null;
            decimal? priorMid = null;
            for (var offset = lookbackCount; offset >= 1; offset--)
            {
                var h4Close = orderedH4[^offset].Close;
                var h4Mid = recentSeries.H4BbMidBandSeries[^offset];
                if (h4Mid > 0m && h4Close >= h4Mid)
                {
                    priorCloseAboveMid = h4Close;
                    priorMid = h4Mid;
                    break;
                }
            }

            if (!priorCloseAboveMid.HasValue)
                return false;

            reason =
                $"PreviousH4Close={priorCloseAboveMid.Value:0.####}, " +
                $"PreviousH4Mid={priorMid!.Value:0.####}, " +
                $"CurrentH4Mid={latestH4Mid:0.####}, " +
                $"LiveM15={liveReferencePrice:0.####}";
            return true;
        }

        private static decimal ResolveScanPrice(CandidateSignalSnapshot snapshot)
        {
            var current = snapshot.Current;

            if (current.DailyBollingerMidBand > 0m && current.DailyBollingerMidDistancePct > -100m)
                return current.DailyBollingerMidBand * (1m + current.DailyBollingerMidDistancePct / 100m);

            if (current.H4BollingerMidBand > 0m && current.H4BollingerMidDistancePct > -100m)
                return current.H4BollingerMidBand * (1m + current.H4BollingerMidDistancePct / 100m);

            return 0m;
        }

        private static DateTime ResolveEntryHistoryStart(DateTime end, int lookbackHours)
        {
            var start = end.AddHours(-Math.Max(1, lookbackHours));

            while (start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                start = start.AddDays(-1);
            }

            return start;
        }

        private static decimal? ResolveSeriesBasedScanPriceFloor(
            decimal scanPrice,
            RecentFeatureSeries recentSeries,
            BollingerStateSet bbState,
            TodayResearchLikePatternKind patternKind)
        {
            if (scanPrice <= 0m)
                return null;

            var dailyUpper = GetLatestValue(recentSeries.DailyBbUpperBandSeries);
            var dailyMid = GetLatestValue(recentSeries.DailyBbMidBandSeries);
            var h4Upper = GetLatestValue(recentSeries.H4BbUpperBandSeries);
            var h4Mid = GetLatestValue(recentSeries.H4BbMidBandSeries);
            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            var dailyUpperSlope = CalculateRelativeSlopePct(recentSeries.DailyBbUpperBandSeries);
            var h4MidSlope = CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries);
            var h4UpperSlope = CalculateRelativeSlopePct(recentSeries.H4BbUpperBandSeries);

            decimal? floor = null;

            if (dailyUpper > 0m && scanPrice >= dailyUpper)
            {
                var retraceFactor = Math.Clamp(
                    (Math.Abs(dailyMidSlope) + Math.Abs(h4MidSlope)) / 20m,
                    0.35m,
                    0.70m);
                floor = dailyUpper + (scanPrice - dailyUpper) * retraceFactor;
            }
            else if (dailyMid > 0m && scanPrice >= dailyMid)
            {
                var retraceFactor = Math.Clamp(
                    (Math.Abs(dailyMidSlope) + Math.Abs(h4MidSlope)) / 24m,
                    0.30m,
                    0.60m);
                floor = dailyMid + (scanPrice - dailyMid) * retraceFactor;
            }
            else if (h4Upper > 0m && scanPrice >= h4Upper)
            {
                var retraceFactor = Math.Clamp(
                    (Math.Abs(h4MidSlope) + Math.Abs(h4UpperSlope)) / 20m,
                    0.25m,
                    0.55m);
                floor = h4Upper + (scanPrice - h4Upper) * retraceFactor;
            }

            if (!floor.HasValue)
                return null;

            if (patternKind == TodayResearchLikePatternKind.BellUp)
                floor = Math.Max(floor.Value, scanPrice * 0.92m);
            else if (patternKind == TodayResearchLikePatternKind.Runaway)
                floor = Math.Max(floor.Value, scanPrice * 0.92m);
            else if (patternKind == TodayResearchLikePatternKind.LaunchContinuation)
                floor = Math.Max(floor.Value, scanPrice * 0.90m);
            else if (patternKind == TodayResearchLikePatternKind.PullbackContinuation)
                floor = Math.Max(floor.Value, scanPrice * 0.88m);

            return decimal.Round(floor.Value, 2, MidpointRounding.AwayFromZero);
        }

        private WishListItem BuildWishListItem(
            StockInfo stock,
            PresetScanCode preset,
            CandidateSignalSnapshot snapshot,
            List<Candle> candles,
            DateTime scanTimeMarket,
            string scanTimeZone,
            WishListScoreResult wishScore)
        {
            _logger.Info(
                $"Scan context build started: {stock.Ticker}. " +
                $"DailyMaSignedDistancePct={_fmt.Generic(snapshot.Current.DailyMaSignedDistancePct)}, " +
                $"WeeklyMaSignedDistancePct={_fmt.Generic(snapshot.Current.WeeklyMaSignedDistancePct ?? 0m)}, " +
                $"DailyMaDelta3={_fmt.Generic(snapshot.DailyMaDelta3)}, " +
                $"H4MaDelta3={_fmt.Generic(snapshot.H4MaDelta3)}, " +
                $"DailyRsiDelta3={_fmt.Generic(snapshot.DailyRsiDelta3)}, " +
                $"DailyMacdDelta3={_fmt.Generic(snapshot.DailyMacdDelta3)}");

            var recentSeries = BuildRecentFeatureSeries(candles);
            var targetForecast = CalculateWishListTargetForecast(
                stock.Ticker,
                snapshot,
                recentSeries,
                candles,
                scanTimeMarket);

            _logger.Info(
                $"Scan context build completed: {stock.Ticker}. " +
                $"ExpectedBarsToTarget={targetForecast.ExpectedBarsToTarget?.ToString() ?? "null"}, " +
                $"ExpectedTargetTime={targetForecast.ExpectedTargetMarketTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null"}");

            return new WishListItem
            {
                Ticker = stock.Ticker,
                Scan = new ScanInfo
                {
                    PresetScanCode = preset.ScanCode,
                    PresetDescription = preset.Description,
                    ScanTime = scanTimeMarket,
                    ScanTimeZone = scanTimeZone
                },
                Score = new ScoreInfo
                {
                    Score = wishScore.TotalScore,
                    WeeklyScore = wishScore.WeeklyScore,
                    DailyScore = wishScore.DailyScore,
                    EntryScore = null
                },
                Context = new MarketContextInfo
                {
                    DistanceTo20dHigh = snapshot.Current.DistanceTo20dHigh,
                    DistanceTo52wHigh = snapshot.Current.DistanceTo52wHigh,
                    DailyRSI14 = snapshot.Current.DailyRSI14,
                    Notes = BuildWishListNotes(snapshot)
                },
                RecentDailyBbUpperBandSeries = recentSeries.DailyBbUpperBandSeries,
                RecentDailyBbMidBandSeries = recentSeries.DailyBbMidBandSeries,
                RecentDailyBbLowerBandSeries = recentSeries.DailyBbLowerBandSeries,
                RecentDailyMacdLineSeries = recentSeries.DailyMacdLineSeries,
                RecentDailyMacdSignalSeries = recentSeries.DailyMacdSignalSeries,
                RecentDailyMacdHistogramSeries = recentSeries.DailyMacdHistogramSeries,
                RecentDailyRsiSeries = recentSeries.DailyRsiSeries,
                RecentWeeklyBbUpperBandSeries = recentSeries.WeeklyBbUpperBandSeries,
                RecentWeeklyBbMidBandSeries = recentSeries.WeeklyBbMidBandSeries,
                RecentWeeklyBbLowerBandSeries = recentSeries.WeeklyBbLowerBandSeries,
                RecentWeeklyMacdLineSeries = recentSeries.WeeklyMacdLineSeries,
                RecentWeeklyMacdSignalSeries = recentSeries.WeeklyMacdSignalSeries,
                RecentWeeklyMacdHistogramSeries = recentSeries.WeeklyMacdHistogramSeries,
                RecentWeeklyRsiSeries = recentSeries.WeeklyRsiSeries,
                RecentH4BbUpperBandSeries = recentSeries.H4BbUpperBandSeries,
                RecentH4BbMidBandSeries = recentSeries.H4BbMidBandSeries,
                RecentH4BbLowerBandSeries = recentSeries.H4BbLowerBandSeries,
                RecentH4MacdLineSeries = recentSeries.H4MacdLineSeries,
                RecentH4MacdSignalSeries = recentSeries.H4MacdSignalSeries,
                RecentH4MacdHistogramSeries = recentSeries.H4MacdHistogramSeries,
                RecentH4RsiSeries = recentSeries.H4RsiSeries,
                FirstSeen = scanTimeMarket,
                LastEvaluatedAt = scanTimeMarket,
                ExpectedTargetTime = targetForecast.ExpectedTargetMarketTime,
                ExpectedBarsToTarget = targetForecast.ExpectedBarsToTarget
            };
        }

        private CandidateDetails BuildCandidateItem(
            StockInfo stock,
            bool isFromWishlist,
            DailyFamilySplit dailyFamilySplit,
            bool needsDeeperEntry,
            bool needsMomentumExit,
            PresetScanCode preset,
            CandidateSignalSnapshot snapshot,
            List<Candle> candles,
            TradePlanInfo trade,
            CandidateDiagnostics diagnostics,
            DateTime scanTimeMarket,
            string scanTimeZone,
            decimal dailyScore,
            decimal weeklyScore,
            decimal entryScore,
            decimal finalScore,
            TodayResearchLikePatternKind todayResearchLikePatternKind,
            decimal todayResearchLikeSeriesScore)
        {
            var recentSeries = BuildRecentFeatureSeries(candles);
            var bbState = BuildBollingerStateSet(recentSeries);
            var bellPatternSignal = ClassifyBellPatternSignal(bbState, recentSeries);

            var nextDayRank = CalculateNextDayRank(
                preset.ScanCode,
                finalScore,
                weeklyScore,
                dailyScore,
                entryScore,
                needsDeeperEntry,
                needsMomentumExit,
                snapshot,
                candles,
                recentSeries,
                bbState,
                dailyFamilySplit,
                todayResearchLikePatternKind,
                todayResearchLikeSeriesScore);

            nextDayRank += CalculateTodayResearchLikeLowProfitRankCompensation(
                preset.ScanCode,
                snapshot,
                candles,
                recentSeries,
                trade);

            return new CandidateDetails
            {
                Ticker = stock.Ticker,
                IsFromWishlist = isFromWishlist,
                RecentDailyCloseSeries = recentSeries.DailyCloseSeries,
                RecentDailyOpenSeries = recentSeries.DailyOpenSeries,
                RecentDailyHighSeries = recentSeries.DailyHighSeries,
                RecentDailyLowSeries = recentSeries.DailyLowSeries,
                RecentDailyBbUpperBandSeries = recentSeries.DailyBbUpperBandSeries,
                RecentDailyBbMidBandSeries = recentSeries.DailyBbMidBandSeries,
                RecentDailyBbLowerBandSeries = recentSeries.DailyBbLowerBandSeries,
                RecentDailyRsiSeries = recentSeries.DailyRsiSeries,
                RecentDailyMacdLineSeries = recentSeries.DailyMacdLineSeries,
                RecentDailyMacdSignalSeries = recentSeries.DailyMacdSignalSeries,
                RecentDailyMacdHistogramSeries = recentSeries.DailyMacdHistogramSeries,
                RecentWeeklyBbUpperBandSeries = recentSeries.WeeklyBbUpperBandSeries,
                RecentWeeklyBbMidBandSeries = recentSeries.WeeklyBbMidBandSeries,
                RecentWeeklyBbLowerBandSeries = recentSeries.WeeklyBbLowerBandSeries,
                RecentWeeklyRsiSeries = recentSeries.WeeklyRsiSeries,
                RecentWeeklyMacdLineSeries = recentSeries.WeeklyMacdLineSeries,
                RecentWeeklyMacdSignalSeries = recentSeries.WeeklyMacdSignalSeries,
                RecentWeeklyMacdHistogramSeries = recentSeries.WeeklyMacdHistogramSeries,
                RecentH4OpenSeries = recentSeries.H4OpenSeries,
                RecentH4HighSeries = recentSeries.H4HighSeries,
                RecentH4LowSeries = recentSeries.H4LowSeries,
                RecentH4CloseSeries = recentSeries.H4CloseSeries,
                RecentH4BbUpperBandSeries = recentSeries.H4BbUpperBandSeries,
                RecentH4BbMidBandSeries = recentSeries.H4BbMidBandSeries,
                RecentH4BbLowerBandSeries = recentSeries.H4BbLowerBandSeries,
                RecentH4RsiSeries = recentSeries.H4RsiSeries,
                RecentH4MacdLineSeries = recentSeries.H4MacdLineSeries,
                RecentH4MacdSignalSeries = recentSeries.H4MacdSignalSeries,
                RecentH4MacdHistogramSeries = recentSeries.H4MacdHistogramSeries,
                WeeklyBbDirection = bbState.Weekly.Direction,
                WeeklyBbRegime = bbState.Weekly.Regime,
                DailyBbDirection = bbState.Daily.Direction,
                DailyBbRegime = bbState.Daily.Regime,
                H4BbDirection = bbState.H4.Direction,
                H4BbRegime = bbState.H4.Regime,
                NeedsDeeperEntry = needsDeeperEntry,
                NeedsMomentumExit = needsMomentumExit,
                IsBellUpPattern = todayResearchLikePatternKind == TodayResearchLikePatternKind.BellUp,
                PatternVerdictReason = bellPatternSignal.Kind switch
                {
                    BellPatternKind.BellUp => $"BellUp confirmed on {bellPatternSignal.Timeframe}",
                    BellPatternKind.Triangle => $"Triangle detected on {bellPatternSignal.Timeframe}",
                    _ => string.Empty
                },
                Scan = new ScanInfo
                {
                    PresetScanCode = preset.ScanCode,
                    PresetDescription = preset.Description,
                    ScanTime = scanTimeMarket,
                    ScanTimeZone = scanTimeZone
                },
                Score = new ScoreInfo
                {
                    Score = finalScore,
                    NextDayRank = nextDayRank,
                    WeeklyScore = weeklyScore,
                    DailyScore = dailyScore,
                    EntryScore = entryScore
                },
                Context = new MarketContextInfo
                {
                    DistanceTo20dHigh = snapshot.Current.DistanceTo20dHigh,
                    DistanceTo52wHigh = snapshot.Current.DistanceTo52wHigh,
                    DailyRSI14 = snapshot.Current.DailyRSI14,
                    Notes = BuildCandidateNotes(snapshot)
                },
                TradePlan = trade,
                Diagnostics = diagnostics
            };
        }

        private decimal CalculateTodayResearchLikeLowProfitRankCompensation(
            string presetScanCode,
            CandidateSignalSnapshot snapshot,
            List<Candle> candles,
            RecentFeatureSeries recentSeries,
            TradePlanInfo trade)
        {
            if (IsBelowPreviousClosedDailyMid(candles))
                return 0m;

            var minPlannedProfitPct = _getCandidatesSettingsProvider.Get().CandidateFilter.MinPlannedProfitPct;
            if (trade.ProfitPercent >= minPlannedProfitPct)
                return 0m;

            var bbState = BuildBollingerStateSet(recentSeries);
            var seriesScore = CalculateTodayResearchLikeSeriesScore(bbState, recentSeries);
            if (seriesScore < 9m)
                return 0m;

            var livePreset =
                string.Equals(presetScanCode, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "TOP_OPEN_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase);

            if (!livePreset)
                return 0m;

            decimal score = 0.25m;

            if (seriesScore >= 10m)
                score += 0.20m;

            if (seriesScore >= 12m)
                score += 0.15m;

            if (trade.ProfitPercent >= minPlannedProfitPct * 0.75m)
                score += 0.10m;

            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            var h4MidSlope = CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries);
            var dailyMacdSlope = CalculateSlope(recentSeries.DailyMacdHistogramSeries);
            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdHistogramSeries);

            if (dailyMidSlope > -12m && h4MidSlope > -12m)
                score += 0.10m;

            if (dailyMacdSlope > -0.20m && h4MacdSlope > -0.15m)
                score += 0.10m;

            return score;
        }

        private static (decimal DefaultProfitPct, decimal MinProfitPct, decimal MaxProfitPct) ResolveAmplitudeAwareResearchLikeProfitProfile(
            TodayResearchLikePatternKind patternKind,
            decimal seriesScore,
            decimal atrRatio,
            decimal defaultProfitPct,
            decimal minProfitPct,
            decimal maxProfitPct)
        {
            var score = Math.Max(seriesScore, 0m);
            decimal defaultBoost = 0m;
            decimal minBoost = 0m;
            decimal maxBoost = 0m;

            if (patternKind == TodayResearchLikePatternKind.Runaway)
            {
                if (score >= 10m)
                {
                    defaultBoost += 0.005m;
                    minBoost += 0.005m;
                    maxBoost += 0.010m;
                }

                if (score >= 12m)
                {
                    defaultBoost += 0.010m;
                    minBoost += 0.010m;
                    maxBoost += 0.020m;
                }

                if (score >= 15m)
                {
                    defaultBoost += 0.010m;
                    minBoost += 0.010m;
                    maxBoost += 0.030m;
                }

                if (atrRatio >= 3.0m)
                {
                    defaultBoost += 0.005m;
                    maxBoost += 0.010m;
                }

                if (atrRatio >= 4.5m)
                    maxBoost += 0.020m;
            }
            else if (patternKind == TodayResearchLikePatternKind.LaunchContinuation)
            {
                if (score >= 9m)
                {
                    defaultBoost += 0.005m;
                    minBoost += 0.004m;
                    maxBoost += 0.010m;
                }

                if (score >= 11m)
                {
                    defaultBoost += 0.008m;
                    minBoost += 0.006m;
                    maxBoost += 0.018m;
                }

                if (score >= 14m)
                {
                    defaultBoost += 0.010m;
                    minBoost += 0.008m;
                    maxBoost += 0.025m;
                }

                if (atrRatio >= 3.0m)
                {
                    defaultBoost += 0.005m;
                    maxBoost += 0.010m;
                }

                if (atrRatio >= 4.5m)
                    maxBoost += 0.020m;
            }
            else if (patternKind == TodayResearchLikePatternKind.BellUp)
            {
                if (score >= 9m)
                {
                    defaultBoost += 0.006m;
                    minBoost += 0.005m;
                    maxBoost += 0.012m;
                }

                if (score >= 11m)
                {
                    defaultBoost += 0.010m;
                    minBoost += 0.008m;
                    maxBoost += 0.020m;
                }

                if (score >= 14m)
                {
                    defaultBoost += 0.012m;
                    minBoost += 0.010m;
                    maxBoost += 0.030m;
                }

                if (atrRatio >= 3.0m)
                {
                    defaultBoost += 0.005m;
                    maxBoost += 0.010m;
                }

                if (atrRatio >= 4.5m)
                    maxBoost += 0.020m;
            }
            else if (patternKind == TodayResearchLikePatternKind.PullbackContinuation)
            {
                if (score >= 8m)
                {
                    defaultBoost += 0.004m;
                    minBoost += 0.003m;
                    maxBoost += 0.010m;
                }

                if (score >= 10m)
                {
                    defaultBoost += 0.006m;
                    minBoost += 0.005m;
                    maxBoost += 0.015m;
                }

                if (score >= 12m)
                {
                    defaultBoost += 0.007m;
                    minBoost += 0.005m;
                    maxBoost += 0.020m;
                }

                if (atrRatio >= 3.5m)
                {
                    defaultBoost += 0.004m;
                    maxBoost += 0.010m;
                }

                if (atrRatio >= 5.0m)
                    maxBoost += 0.020m;
            }
            else if (score >= 10m)
            {
                maxBoost += 0.010m;
            }

            var adjustedMax = Math.Min(maxProfitPct + maxBoost, 0.18m);
            var adjustedDefault = Math.Min(defaultProfitPct + defaultBoost, adjustedMax);
            var adjustedMin = Math.Min(minProfitPct + minBoost, adjustedDefault);

            return (
                decimal.Round(adjustedDefault, 4, MidpointRounding.AwayFromZero),
                decimal.Round(adjustedMin, 4, MidpointRounding.AwayFromZero),
                decimal.Round(adjustedMax, 4, MidpointRounding.AwayFromZero));
        }

        private static decimal CalculateTodayResearchLikePatternRankBonus(
            string presetScanCode,
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            TodayResearchLikePatternKind patternKind,
            decimal seriesScore)
        {
            if (patternKind == TodayResearchLikePatternKind.None)
                return 0m;

            if (!IsLiveMoverPreset(presetScanCode) && !IsGainPreset(presetScanCode))
                return 0m;

            var score = 0m;

            score += patternKind switch
            {
                TodayResearchLikePatternKind.Runaway => 0.42m,
                TodayResearchLikePatternKind.LaunchContinuation => 0.36m,
                TodayResearchLikePatternKind.BellUp => 0.40m,
                _ => 0.30m
            };

            if (seriesScore >= 10m)
                score += patternKind == TodayResearchLikePatternKind.BellUp ? 0.13m :
                         patternKind == TodayResearchLikePatternKind.LaunchContinuation ? 0.12m : 0.10m;

            if (seriesScore >= 12m)
                score += patternKind == TodayResearchLikePatternKind.Runaway ? 0.12m : 0.10m;

            if (seriesScore >= 15m)
                score += patternKind == TodayResearchLikePatternKind.BellUp ? 0.15m :
                         patternKind == TodayResearchLikePatternKind.LaunchContinuation ? 0.14m : 0.12m;

            if (diagnostics.ATRRatio >= 3.0m)
                score += 0.08m;

            if (diagnostics.ATRRatio >= 4.5m)
                score += 0.08m;

            if (snapshot.Current.DailyRSI14 >= 40m && snapshot.Current.DailyRSI14 <= 62m)
                score += patternKind == TodayResearchLikePatternKind.Runaway ? 0.08m :
                         patternKind == TodayResearchLikePatternKind.BellUp ? 0.07m : 0.06m;

            if (snapshot.Current.DistanceTo20dHigh <= -8m)
                score += 0.05m;

            if (snapshot.Current.DailyMaSignedDistancePct > 0m)
                score += 0.05m;

            var weeklyMacdHistDelta = diagnostics.WeeklyMACDHistDelta ?? 0m;
            if (weeklyMacdHistDelta >= 0m)
                score += 0.05m;

            return decimal.Round(Math.Min(score, 0.90m), 4, MidpointRounding.AwayFromZero);
        }

        private decimal CalculateNextDayRank(
            string presetScanCode,
            decimal candidateScore,
            decimal weeklyScore,
            decimal dailyScore,
            decimal entryScore,
            bool needsDeeperEntry,
            bool needsMomentumExit,
            CandidateSignalSnapshot snapshot,
            List<Candle> candles,
            RecentFeatureSeries recentSeries,
            BollingerStateSet bbState,
            DailyFamilySplit dailyFamilySplit,
            TodayResearchLikePatternKind todayResearchLikePatternKind,
            decimal todayResearchLikeSeriesScore)
        {
            var s = _nextDayRankingSettings;
            var diagnostics = BuildDiagnostics(snapshot, candles);

            var entryScoreNorm = Clamp01(entryScore / s.EntryScoreNormMax);
            var candidateScoreNorm = Clamp01(candidateScore / s.CandidateScoreNormMax);
            var trendPositionNorm = Clamp01(diagnostics.TrendPosition / s.TrendPositionNormMax);
            var dailyTrendPositionNorm = Clamp01(diagnostics.DailyTrendPosition / s.DailyTrendPositionNormMax);
            var atrRatioNorm = Clamp01(diagnostics.ATRRatio / s.AtrRatioNormMax);
            var bbMidNorm = Clamp01(diagnostics.BBMidSignedDistancePct / s.BbMidNormMax);
            var presetBonus = ResolvePresetBonus(presetScanCode, s);

            var score =
                s.EntryScoreWeight * entryScoreNorm +
                s.CandidateScoreWeight * candidateScoreNorm +
                s.TrendPositionWeight * trendPositionNorm +
                s.DailyTrendPositionWeight * dailyTrendPositionNorm +
                s.AtrRatioWeight * atrRatioNorm +
                s.BbMidWeight * bbMidNorm +
                s.PresetWeight * presetBonus;

            if (entryScore >= s.EntryScoreBonusThreshold)
                score += s.EntryScoreBonus;

            if (diagnostics.TrendPosition >= s.TrendPositionBonusThreshold)
                score += s.TrendPositionBonus;

            if (diagnostics.DailyTrendPosition >= s.DailyTrendPositionBonusThreshold)
                score += s.DailyTrendPositionBonus;

            if (diagnostics.ATRRatio >= s.AtrRatioBonusThreshold)
                score += s.AtrRatioBonus;

            score += CalculateBbRankAdjustment(
                bbState.Weekly,
                bbState.Daily,
                bbState.H4);

            if (needsDeeperEntry)
                score += s.DeeperEntryBonus;

            if (needsMomentumExit)
                score -= s.MomentumExitPenalty;

            if (IsStrongMinFirstProxy(snapshot, diagnostics, needsDeeperEntry, needsMomentumExit))
                score += s.StrongMinFirstBonus;

            if (IsExplosiveMinFirstProxy(snapshot, diagnostics, needsDeeperEntry, needsMomentumExit))
                score += s.ExplosiveMinFirstBonus;

            if (IsConstructiveDeepMinFirstProxy(snapshot, diagnostics, needsDeeperEntry))
                score += s.ConstructiveDeepMinFirstBonus;

            if (IsParabolicExpansionProxy(snapshot, diagnostics, needsDeeperEntry, needsMomentumExit))
                score += s.ParabolicExpansionBonus;

            if (IsDeepParabolicExpansionProxy(snapshot, diagnostics, needsDeeperEntry))
                score += s.DeepParabolicExpansionBonus;

            if (IsDeepLaunchProxy(snapshot))
                score += s.DeepLaunchBonus;

            if (IsExplosiveBreakoutProxy(snapshot))
                score += s.ExplosiveBreakoutBonus;

            var isEarlyReversal = IsEarlyReversalProxy(snapshot, diagnostics, s);
            if (isEarlyReversal)
                score += s.EarlyReversalBonus;

            if (IsReversalDeepHookProxy(snapshot, diagnostics, s))
                score += s.ReversalDeepHookBonus;

            if (IsReversalH4BellUpHybridProxy(snapshot, diagnostics, s, recentSeries, bbState))
                score += s.ReversalH4BellUpHybridBonus;

            if (dailyFamilySplit == DailyFamilySplit.Reversal &&
                IsReversalHighAmplitudeProxy(snapshot, diagnostics, s))
            {
                score += s.ReversalHighAmplitudeBonus;

                if (diagnostics.ATRRatio >= s.ReversalHighAmplitudeStrongAtrRatio)
                    score += s.ReversalHighAmplitudeStrongBonus;
            }

            if (dailyFamilySplit == DailyFamilySplit.Reversal &&
                IsReversalConstructiveDeepBounceProxy(snapshot, diagnostics, s, candidateScore, entryScore))
            {
                score += s.ReversalConstructiveDeepBounceBonus;
            }

            if (dailyFamilySplit == DailyFamilySplit.Reversal &&
                IsReversalBrokenDownProxy(snapshot, diagnostics, s))
            {
                score -= s.ReversalBrokenDownPenalty;
            }

            if (dailyFamilySplit == DailyFamilySplit.Reversal &&
                IsReversalMatureWeakBounceProxy(snapshot, diagnostics, s))
            {
                score -= s.ReversalMatureWeakBouncePenalty;
            }

            if (dailyFamilySplit == DailyFamilySplit.Reversal &&
                IsReversalSeriesRecoveryProxy(snapshot, diagnostics, recentSeries, s))
            {
                score += s.ReversalSeriesRecoveryBonus;
            }

            if (dailyFamilySplit == DailyFamilySplit.Reversal &&
                IsReversalWeakContinuationProxy(snapshot, recentSeries, s))
            {
                score -= s.ReversalWeakContinuationPenalty;
            }

            if (IsWeakDeepPullbackProxy(snapshot, diagnostics, needsDeeperEntry))
                score -= s.WeakDeepPullbackPenalty;

            if (!isEarlyReversal &&
                diagnostics.DailyTrendPosition < s.DailyTrendNegativePenaltyThreshold)
            {
                score -= s.DailyTrendNegativePenalty;
            }

            if (!isEarlyReversal &&
                diagnostics.BBMidSignedDistancePct < s.BbMidNegativePenaltyThreshold)
            {
                score -= s.BbMidNegativePenalty;
            }

            if (snapshot.Current.DistanceTo20dHigh >= s.LateExtensionDistanceTo20dHighThreshold &&
                snapshot.Current.DailyRSI14 >= s.LateExtensionDailyRsi14Threshold)
            {
                score -= s.LateExtensionPenalty;
            }

            if (snapshot.Current.DailyRSI14 >= s.LateContinuationDailyRsi14Threshold &&
                snapshot.Current.DistanceTo20dHigh >= s.LateContinuationDistanceTo20dHighThreshold &&
                diagnostics.TrendPosition >= s.LateContinuationTrendPositionThreshold &&
                diagnostics.BBMidSignedDistancePct >= s.LateContinuationBbMidThreshold)
            {
                score -= s.LateContinuationPenalty;
            }

            if (diagnostics.TrendPosition >= s.OverextendedTrendPositionThreshold &&
                snapshot.Current.DailyRSI14 >= s.OverextendedDailyRsi14Threshold &&
                diagnostics.BBMidSignedDistancePct >= s.OverextendedBbMidThreshold)
            {
                score -= s.OverextendedPenalty;
            }

            if (IsExhaustedMoverProxy(snapshot, diagnostics, s))
                score -= s.ExhaustedMoverPenalty;

            var isExplosiveBellUpAnomalyBypass = IsExplosiveBellUpAnomalyBypassProxy(
                snapshot,
                diagnostics,
                s,
                recentSeries,
                bbState);
            if (isExplosiveBellUpAnomalyBypass)
                score += s.ExplosiveBellUpAnomalyBypassBonus;

            if (IsAnomalousVolatilityProxy(diagnostics, s) &&
                !isExplosiveBellUpAnomalyBypass)
            {
                score -= s.AnomalousVolatilityPenalty;
            }

            score += CalculateHighAmplitudeProxyAdjustment(
                snapshot,
                diagnostics,
                s,
                todayResearchLikePatternKind);

            score += CalculatePatternSeriesAdjustment(recentSeries, s);

            if (IsResearchLikeLaunch(snapshot, diagnostics, recentSeries, s))
            {
                score += s.ResearchLikeBonus;

                if (HasPositiveSlope(recentSeries.DailyMacdHistogramSeries, s.ResearchLikeDailyMacdSlopeThreshold) &&
                    HasPositiveSlope(recentSeries.H4RsiSeries, s.PatternH4RsiSlopeThreshold))
                {
                    score += s.ResearchLikeStrongPatternBonus;
                }

                if (HasPositiveSlope(recentSeries.H4MacdHistogramSeries, 0.05m) &&
                    CountUpMoves(recentSeries.H4RsiSeries) >= 7)
                {
                    score += s.ResearchLikeExtraBonus;
                }
            }

            if (IsResearchLikePreLaunchRankProxy(snapshot, diagnostics, recentSeries))
            {
                score += 0.85m;

                if (presetScanCode is "TOP_PERC_GAIN" or "TOP_OPEN_PERC_GAIN")
                    score += 0.20m;

                if (diagnostics.ATRRatio >= 2.5m)
                    score += 0.15m;
            }

            if (IsShortHistoryLiveMoverRankProxy(presetScanCode, snapshot, diagnostics, recentSeries))
            {
                score += 1.10m;

                if (presetScanCode is "TOP_PERC_GAIN" or "TOP_OPEN_PERC_GAIN")
                    score += 0.25m;

                if (diagnostics.ATRRatio >= 4.0m)
                    score += 0.20m;
            }

            score += CalculateTodayResearchLikePatternRankBonus(
                presetScanCode,
                snapshot,
                diagnostics,
                todayResearchLikePatternKind,
                todayResearchLikeSeriesScore);

            score += CalculateTodayResearchLikeFreshnessAdjustment(
                presetScanCode,
                snapshot,
                recentSeries);

            return decimal.Round(score, 4, MidpointRounding.AwayFromZero);
        }

        private static decimal CalculateHighAmplitudeProxyAdjustment(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings,
            TodayResearchLikePatternKind todayResearchLikePatternKind)
        {
            var score = 0m;
            var constructiveRsi =
                snapshot.Current.DailyRSI14 >= settings.HighAmplitudeProxyConstructiveRsiMin &&
                snapshot.Current.DailyRSI14 <= settings.HighAmplitudeProxyConstructiveRsiMax;
            var deepEnough =
                snapshot.Current.DistanceTo20dHigh <= settings.HighAmplitudeProxyDeepDistanceTo20dHigh ||
                diagnostics.Pullback10d <= settings.HighAmplitudeProxyDeepPullback10d ||
                diagnostics.DailyPullback10d <= settings.HighAmplitudeProxyDeepPullback10d;

            if (diagnostics.ATRRatio >= settings.HighAmplitudeProxyMinAtrRatio &&
                constructiveRsi &&
                deepEnough)
            {
                score += settings.HighAmplitudeProxyBonus;

                if (diagnostics.ATRRatio >= settings.HighAmplitudeProxyStrongAtrRatio)
                    score += settings.HighAmplitudeProxyStrongBonus;
            }

            var runawayLike =
                todayResearchLikePatternKind != TodayResearchLikePatternKind.None ||
                diagnostics.TrendPosition >= settings.RunawayHighAmplitudeTrendPositionThreshold ||
                diagnostics.DailyTrendPosition >= settings.RunawayHighAmplitudeTrendPositionThreshold;

            if (runawayLike &&
                diagnostics.ATRRatio >= settings.HighAmplitudeProxyMinAtrRatio &&
                diagnostics.TrendPosition >= settings.RunawayHighAmplitudeTrendPositionThreshold &&
                constructiveRsi &&
                !IsExhaustedMoverProxy(snapshot, diagnostics, settings))
            {
                score += settings.RunawayHighAmplitudeTrendBonus;
            }

            var shallowLowEnergy =
                diagnostics.ATRRatio <= settings.LowAmplitudeProxyMaxAtrRatio &&
                snapshot.Current.DistanceTo20dHigh > settings.HighAmplitudeProxyDeepDistanceTo20dHigh &&
                diagnostics.Pullback10d > settings.LowAmplitudeProxyShallowPullback10d &&
                diagnostics.DailyPullback10d > settings.LowAmplitudeProxyShallowPullback10d;

            if (shallowLowEnergy)
                score -= settings.LowAmplitudeProxyPenalty;

            return score;
        }

        private static bool IsAnomalousVolatilityProxy(
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings)
        {
            return diagnostics.ATRRatio >= settings.AnomalousVolatilityAtrRatioThreshold &&
                   diagnostics.VolumeRatio20 <= settings.AnomalousVolatilityMaxVolumeRatio20;
        }

        private static bool IsExplosiveBellUpAnomalyBypassProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings,
            RecentFeatureSeries recentSeries,
            BollingerStateSet bbState)
        {
            var bellPatternSignal = ClassifyBellPatternSignal(bbState, recentSeries);
            if (bellPatternSignal.Kind != BellPatternKind.BellUp)
                return false;

            return diagnostics.ATRRatio >= settings.ExplosiveBellUpAnomalyMinAtrRatio &&
                   diagnostics.TrendPosition >= settings.ExplosiveBellUpAnomalyMinTrendPosition &&
                   diagnostics.DailyTrendPosition >= settings.ExplosiveBellUpAnomalyMinTrendPosition &&
                   snapshot.Current.DailyRSI14 <= settings.ExplosiveBellUpAnomalyMaxDailyRsi14 &&
                   snapshot.Current.DistanceTo20dHigh <= settings.ExplosiveBellUpAnomalyMaxDistanceTo20dHigh &&
                   (diagnostics.Pullback10d <= settings.HighAmplitudeProxyDeepPullback10d ||
                    diagnostics.DailyPullback10d <= settings.HighAmplitudeProxyDeepPullback10d);
        }

        private static bool IsExhaustedMoverProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings)
        {
            return snapshot.Current.DailyRSI14 >= settings.ExhaustedMoverDailyRsi14Threshold &&
                   (diagnostics.TrendPosition >= settings.ExhaustedMoverTrendPositionThreshold ||
                    diagnostics.DailyTrendPosition >= settings.ExhaustedMoverDailyTrendPositionThreshold);
        }

        private static bool IsResearchLikePreLaunchRankProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            RecentFeatureSeries recentSeries)
        {
            var current = snapshot.Current;
            var dailyDistance = current.DailyMaSignedDistancePct;
            var weeklyDistance = current.WeeklyMaSignedDistancePct ?? 0m;

            if (dailyDistance < -55m || dailyDistance > 8m)
                return false;

            if (current.DistanceTo20dHigh > -8m)
                return false;

            if (current.DailyRSI14 < 35m || current.DailyRSI14 > 62m)
                return false;

            if (current.WeeklyMACDLineMinusSignal.HasValue &&
                current.WeeklyMACDLineMinusSignal.Value > 0.8m)
            {
                return false;
            }

            var h4MacdLast = recentSeries.H4MacdHistogramSeries.LastOrDefault();
            var dailyMacdLast = recentSeries.DailyMacdHistogramSeries.LastOrDefault();
            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            var h4MidSlope = CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries);
            var h4UpperSlope = CalculateRelativeSlopePct(recentSeries.H4BbUpperBandSeries);

            var dailyTryingToTurn =
                snapshot.DailyRsiDelta3 >= -5m &&
                dailyMacdLast > -0.75m;

            var h4NotBreakingDown =
                h4MacdLast > -0.65m &&
                dailyMidSlope > -10m &&
                h4MidSlope > -10m &&
                h4UpperSlope > -10m;

            return dailyTryingToTurn &&
                   h4NotBreakingDown &&
                   (weeklyDistance > -30m || diagnostics.ATRRatio >= 2.5m);
        }

        private static bool IsShortHistoryLiveMoverRankProxy(
            string presetScanCode,
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            RecentFeatureSeries recentSeries)
        {
            var livePreset =
                string.Equals(presetScanCode, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "TOP_OPEN_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase);

            if (!livePreset)
                return false;

            var hasUsableWeeklyBb =
                recentSeries.WeeklyBbMidBandSeries.Count >= 3 &&
                recentSeries.WeeklyBbUpperBandSeries.Count >= 3 &&
                recentSeries.WeeklyBbLowerBandSeries.Count >= 3;

            if (hasUsableWeeklyBb)
                return false;

            var current = snapshot.Current;
            var dailyDistance = current.DailyMaSignedDistancePct;
            var h4Distance = current.H4MaSignedDistancePct;

            if (dailyDistance < -35m || dailyDistance > 320m)
                return false;

            if (h4Distance < -35m)
                return false;

            if (current.DistanceTo20dHigh > -0.25m)
                return false;

            if (current.DailyRSI14 < 35m || current.DailyRSI14 > 92m)
                return false;

            if (current.WeeklyMACDLineMinusSignal.HasValue &&
                current.WeeklyMACDLineMinusSignal.Value < -3.0m)
            {
                return false;
            }

            var dailyTurningOrExplosive =
                snapshot.DailyRsiDelta3 >= 8m ||
                current.DailyMACDLineMinusSignal >= -0.20m ||
                diagnostics.ATRRatio >= 4m;

            var h4Confirming =
                CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries) >= -5m ||
                CalculateRelativeSlopePct(recentSeries.H4BbUpperBandSeries) >= -5m ||
                current.MACDLineMinusSignal >= -0.50m;

            var momentumConfirming =
                current.DailyMACDLineMinusSignal >= -0.75m &&
                current.MACDLineMinusSignal >= -0.65m &&
                diagnostics.ATRRatio >= 2.0m;

            return dailyTurningOrExplosive &&
                   h4Confirming &&
                   momentumConfirming;
        }

        private decimal CalculateTodayResearchLikeFreshnessAdjustment(
            string presetScanCode,
            CandidateSignalSnapshot snapshot,
            RecentFeatureSeries recentSeries)
        {
            var livePreset =
                string.Equals(presetScanCode, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "TOP_OPEN_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase);

            if (!livePreset)
                return 0m;

            var weeklyMacdLast = recentSeries.WeeklyMacdHistogramSeries.LastOrDefault();
            var dailyMacdLast = recentSeries.DailyMacdHistogramSeries.LastOrDefault();
            var h4MacdLast = recentSeries.H4MacdHistogramSeries.LastOrDefault();
            var weeklyMidSlope = CalculateRelativeSlopePct(recentSeries.WeeklyBbMidBandSeries);
            var weeklyUpperSlope = CalculateRelativeSlopePct(recentSeries.WeeklyBbUpperBandSeries);
            var weeklyLowerSlope = CalculateRelativeSlopePct(recentSeries.WeeklyBbLowerBandSeries);
            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            var dailyUpperSlope = CalculateRelativeSlopePct(recentSeries.DailyBbUpperBandSeries);
            var dailyLowerSlope = CalculateRelativeSlopePct(recentSeries.DailyBbLowerBandSeries);
            var h4MidSlope = CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries);
            var h4UpperSlope = CalculateRelativeSlopePct(recentSeries.H4BbUpperBandSeries);
            var h4LowerSlope = CalculateRelativeSlopePct(recentSeries.H4BbLowerBandSeries);
            var weeklyMacdSlope = CalculateSlope(recentSeries.WeeklyMacdHistogramSeries);
            var dailyMacdSlope = CalculateSlope(recentSeries.DailyMacdHistogramSeries);
            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdHistogramSeries);
            var dailyRsiSlope = CalculateSlope(recentSeries.DailyRsiSeries);
            var h4RsiSlope = CalculateSlope(recentSeries.H4RsiSeries);
            var realWeeklyLaunch = IsRealBollingerLaunch(weeklyMidSlope, weeklyUpperSlope, weeklyLowerSlope);
            var realDailyLaunch = IsRealBollingerLaunch(dailyMidSlope, dailyUpperSlope, dailyLowerSlope);
            var realH4Launch = IsRealBollingerLaunch(h4MidSlope, h4UpperSlope, h4LowerSlope);
            var topPercGain = string.Equals(presetScanCode, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase);
            var hotByVolume = string.Equals(presetScanCode, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase);
            var mostActive = string.Equals(presetScanCode, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase);

            decimal score = 0m;

            if (realDailyLaunch)
                score += 0.45m;
            if (realH4Launch)
                score += 0.40m;
            if (realWeeklyLaunch)
                score += 0.20m;

            if (dailyMidSlope > 0m && dailyUpperSlope > 0m)
                score += 0.25m;
            else if (dailyMidSlope < -8m || dailyUpperSlope < -8m)
                score -= 0.35m;

            if (h4MidSlope > 0m && h4UpperSlope > 0m)
                score += 0.22m;
            else if (h4MidSlope < -8m || h4UpperSlope < -8m)
                score -= 0.30m;

            if (dailyMacdLast > 0m)
                score += 0.20m;
            if (h4MacdLast >= 0m)
                score += 0.15m;

            if (dailyMacdSlope > 0m)
                score += 0.15m;
            else if (dailyMacdSlope < -0.20m)
                score -= 0.20m;

            if (h4MacdSlope > 0m)
                score += 0.10m;
            else if (h4MacdSlope < -0.15m)
                score -= 0.15m;

            if (dailyRsiSlope > 0m)
                score += 0.10m;
            if (h4RsiSlope > 0m)
                score += 0.10m;

            var realContinuation =
                (realDailyLaunch || dailyMidSlope > 0m || dailyUpperSlope > 0m) &&
                (realH4Launch || h4MidSlope > 0m || h4UpperSlope > 0m) &&
                dailyMacdLast > 0m &&
                h4MacdLast >= -0.05m &&
                dailyMacdSlope > -0.08m &&
                h4MacdSlope > -0.08m;

            if (realContinuation)
                score += 0.35m;

            if (realContinuation && topPercGain)
                score += 0.16m;
            if (realContinuation && hotByVolume)
                score += 0.12m;
            if (realContinuation && mostActive)
                score += 0.08m;

            var weakRealContinuation =
                !realDailyLaunch &&
                !realH4Launch &&
                dailyMidSlope <= 0m &&
                h4MidSlope <= 0m &&
                dailyMacdLast <= 0.08m &&
                h4MacdLast <= 0.05m &&
                dailyRsiSlope <= 0m &&
                h4RsiSlope <= 0m;

            if (weakRealContinuation)
                score -= 0.45m;

            var coolingRealContinuation =
                (dailyMidSlope < -8m || dailyUpperSlope < -8m) &&
                (h4MidSlope < -8m || h4UpperSlope < -8m) &&
                dailyMacdSlope <= -0.08m &&
                h4MacdSlope <= -0.08m;

            if (coolingRealContinuation)
                score -= 0.40m;

            var strongAmplitudeProxy =
                (realDailyLaunch || realH4Launch) &&
                weeklyMacdLast > -0.05m &&
                dailyMacdLast > 0.08m &&
                h4MacdLast >= 0m &&
                weeklyMacdSlope > -0.05m &&
                dailyMacdSlope > -0.05m &&
                h4MacdSlope > -0.05m &&
                dailyRsiSlope > 0m &&
                h4RsiSlope > 0m;

            if (strongAmplitudeProxy)
                score += 0.34m;

            if (strongAmplitudeProxy && topPercGain)
                score += 0.18m;
            if (strongAmplitudeProxy && hotByVolume)
                score += 0.14m;
            if (strongAmplitudeProxy && mostActive)
                score += 0.10m;

            return score;
        }

        // Validated 2026-07-13 against evaluation-dataset.csv: Daily/H4 Bollinger mid+upper slope predicts Runaway amplitude (AUC ~0.84 on extreme groups); Daily band compression + lower-band hook slope predicts Reversal amplitude (AUC ~0.63). Series-template distance-matching (below) showed no such signal and no longer drives ranking; it stays for its diagnostics only.
        private const decimal QualityScoreRankWeight = 50m;

        private List<CandidateDetails> ReRankCandidates(
            List<CandidateDetails> candidates,
            SeriesTemplateFamily family)
        {
            if (candidates.Count <= 1)
                return candidates;

            var ordered = candidates
                .OrderByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
                .ThenByDescending(x => x.TradePlan.ProfitPercent)
                .ThenByDescending(x => x.Score.Score)
                .ToList();

            var ranked = ordered
                .Select(x =>
                {
                    var qualityScore = family == SeriesTemplateFamily.TodayResearchLike
                        ? CalculateRunawayLaunchQualityScore(x)
                        : CalculateReversalHookQualityScore(x);

                    if (x.Diagnostics != null)
                    {
                        x.Diagnostics.RankingQualityScore = qualityScore;
                        x.Diagnostics.EstimatedHitRatePct = EstimateHitRatePct(qualityScore, family);
                    }

                    var adjustedRank =
                        qualityScore * QualityScoreRankWeight +
                        (x.Score.NextDayRank ?? 0m);

                    return new
                    {
                        Candidate = x,
                        AdjustedRank = adjustedRank
                    };
                })
                .OrderByDescending(x => x.AdjustedRank)
                .ThenByDescending(x => x.Candidate.TradePlan.ProfitPercent)
                .ThenByDescending(x => x.Candidate.Score.Score)
                .ToList();

            foreach (var item in ranked)
            {
                item.Candidate.Score.NextDayRank = decimal.Round(
                    item.AdjustedRank,
                    4,
                    MidpointRounding.AwayFromZero);
            }

            return ranked.Select(x => x.Candidate).ToList();
        }

        private static decimal CalculateRunawayLaunchQualityScore(CandidateDetails candidate)
        {
            var dailyMidSlope = CalculateTailRelativeSlopePct(candidate.RecentDailyBbMidBandSeries, 4);
            var dailyUpperSlope = CalculateTailRelativeSlopePct(candidate.RecentDailyBbUpperBandSeries, 4);
            var h4MidSlope = CalculateTailRelativeSlopePct(candidate.RecentH4BbMidBandSeries, 4);
            var h4UpperSlope = CalculateTailRelativeSlopePct(candidate.RecentH4BbUpperBandSeries, 4);

            var h4Upper = candidate.RecentH4BbUpperBandSeries;
            var h4Lower = candidate.RecentH4BbLowerBandSeries;
            var h4WidthExpansionPct = 0m;
            if (h4Upper.Count >= 4 && h4Lower.Count >= 4)
            {
                var currentWidth = h4Upper[^1] - h4Lower[^1];
                var previousWidth = h4Upper[^4] - h4Lower[^4];
                if (previousWidth != 0m)
                    h4WidthExpansionPct = (currentWidth / previousWidth - 1m) * 100m;
            }

            var lateH4Penalty = BellUpLateEntryPenalty.Calculate(
                candidate.RecentH4OpenSeries,
                candidate.RecentH4HighSeries,
                candidate.RecentH4LowSeries,
                candidate.RecentH4CloseSeries);
            var lateDailyPenalty = BellUpLateEntryPenalty.Calculate(
                candidate.RecentDailyOpenSeries,
                candidate.RecentDailyHighSeries,
                candidate.RecentDailyLowSeries,
                candidate.RecentDailyCloseSeries);

            return dailyMidSlope * 1.0m +
                   dailyUpperSlope * 0.5m +
                   h4MidSlope * 0.5m +
                   h4UpperSlope * 0.75m +
                   h4WidthExpansionPct * 0.25m -
                   Math.Max(lateH4Penalty, lateDailyPenalty);
        }

        private static decimal CalculateReversalHookQualityScore(CandidateDetails candidate)
        {
            var upper = candidate.RecentDailyBbUpperBandSeries;
            var lower = candidate.RecentDailyBbLowerBandSeries;

            var compressionInverse = 0m;
            if (upper.Count >= 4 && lower.Count >= 4)
            {
                var currentWidth = upper[^1] - lower[^1];
                var previousWidth = upper[^4] - lower[^4];
                if (previousWidth != 0m)
                    compressionInverse = 1m - currentWidth / previousWidth;
            }

            var lowerHookSlope = CalculateTailRelativeSlopePct(lower, 3);

            return compressionInverse * 30m + lowerHookSlope * 0.5m;
        }

        // Coarse, honest buckets from a thin historical sample (evaluation-dataset.csv). Runaway buckets
        // rechecked 2026-09-01 against 422 decided Win/Loss rows after adding the H4 upper-band slope and
        // H4 width-expansion terms to CalculateRunawayLaunchQualityScore (AUC 0.62 -> 0.69 on that change,
        // stable across a chronological split): >=25 -> 88.9% (n=45), [10,25) -> 53.8% (n=93), else -> ~31%
        // (n=284 combined, 32.8%/29.5% either side of 0) observed AmplitudePct>=10% rate. Reversal buckets
        // still reflect the original 2026-07-14 check (n=19) and are left as-is: the called-for recheck
        // happened 2026-09-01 against 260 decided Win/Loss rows and CalculateReversalHookQualityScore came
        // back AUC 0.51 (no signal) - see docs/project-skills/ibswingtrader-scanner/references/REVERSAL_EDGE.md
        // "Open items". Recalibrating these buckets would be tuning noise, not a probability; don't touch
        // them without a score that has shown real signal first. This is a rough historical hit-rate
        // readout, not a statistically calibrated probability - recheck and adjust these breakpoints/rates
        // as more days of evaluation data accumulate rather than trusting them as fixed truth.
        private static decimal EstimateHitRatePct(decimal qualityScore, SeriesTemplateFamily family)
        {
            if (family == SeriesTemplateFamily.TodayResearchLike)
            {
                if (qualityScore >= 25m)
                    return 85m;
                if (qualityScore >= 10m)
                    return 55m;
                return 30m;
            }

            return qualityScore >= 15m
                ? 50m
                : 30m;
        }

        private static bool TryCalculateRealBollingerEnvelope(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower,
            out RealBollingerEnvelope envelope)
            => BellPatternClassifier.TryCalculateRealBollingerEnvelope(upper, mid, lower, out envelope);

        private bool PassFinalAmplitudeProxyGate(
            WishListContext ctx,
            out string reason)
        {
            reason = string.Empty;

            var recentSeries = BuildRecentFeatureSeries(ctx.Candles);
            if (!TryCalculateRealBollingerEnvelope(
                    recentSeries.DailyBbUpperBandSeries,
                    recentSeries.DailyBbMidBandSeries,
                    recentSeries.DailyBbLowerBandSeries,
                    out var daily) ||
                !TryCalculateRealBollingerEnvelope(
                    recentSeries.WeeklyBbUpperBandSeries,
                    recentSeries.WeeklyBbMidBandSeries,
                    recentSeries.WeeklyBbLowerBandSeries,
                    out var weekly) ||
                !TryCalculateRealBollingerEnvelope(
                    recentSeries.H4BbUpperBandSeries,
                    recentSeries.H4BbMidBandSeries,
                    recentSeries.H4BbLowerBandSeries,
                    out var h4))
            {
                return true;
            }

            var dailyProxy =
                daily.UpperMovePct > daily.MidMovePct &&
                daily.MidMovePct >= 0m &&
                daily.OpenPct > 0m;
            var weeklyProxy =
                weekly.UpperMovePct > weekly.MidMovePct &&
                weekly.MidMovePct >= 0m &&
                weekly.OpenPct > 0m;
            var h4Proxy =
                h4.UpperMovePct > h4.MidMovePct &&
                h4.MidMovePct >= 0m &&
                h4.OpenPct > 0m;

            if (dailyProxy || weeklyProxy || h4Proxy)
                return true;

            reason = "Final amplitude proxy rejected: no row-based envelope expansion on daily/weekly/H4.";
            return false;
        }

        private decimal CalculatePatternSeriesAdjustment(
            RecentFeatureSeries recentSeries,
            NextDayRankingSettings settings)
        {
            decimal score = 0m;

            var constructiveDaily =
                HasPositiveRelativeSlope(recentSeries.DailyBbMidBandSeries, settings.PatternDailyMaSlopeThreshold) &&
                HasPositiveSlope(recentSeries.DailyRsiSeries, settings.PatternDailyRsiSlopeThreshold) &&
                CountUpMoves(recentSeries.DailyRsiSeries) >= 3;

            if (constructiveDaily)
                score += settings.PatternConstructiveLaunchBonus;

            var constructiveH4 =
                HasPositiveRelativeSlope(recentSeries.H4BbMidBandSeries, settings.PatternH4MaSlopeThreshold) &&
                HasPositiveSlope(recentSeries.H4RsiSeries, settings.PatternH4RsiSlopeThreshold) &&
                CountUpMoves(recentSeries.H4RsiSeries) >= 6;

            if (constructiveH4)
                score += settings.PatternH4TrendBonus;

            if (IsExhausted(recentSeries.H4RsiSeries, settings.PatternExhaustionH4RsiThreshold) ||
                IsRollingOver(recentSeries.H4BbMidBandSeries))
            {
                score -= settings.PatternExhaustionPenalty;
            }

            return score;
        }

        private RecentFeatureSeries BuildRecentFeatureSeries(List<Candle> candles)
        {
            var scanIndex = candles.Count - 1;

            return new RecentFeatureSeries
            {
                DailyCloseSeries = BuildRecentDailyCloseSeries(candles),
                DailyOpenSeries = BuildRecentDailyCandleSeries(candles, scanIndex, x => x.Open),
                DailyHighSeries = BuildRecentDailyCandleSeries(candles, scanIndex, x => x.High),
                DailyLowSeries = BuildRecentDailyCandleSeries(candles, scanIndex, x => x.Low),
                DailyBbUpperBandSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyBollingerUpperBand),
                DailyBbMidBandSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyBollingerMidBand),
                DailyBbLowerBandSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyBollingerLowerBand),
                DailyRsiSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyRSI14),
                DailyMacdLineSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyMACDLine),
                DailyMacdSignalSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyMACDSignal),
                DailyMacdHistogramSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyMACDHistogram),
                WeeklyBbUpperBandSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyBollingerUpperBand),
                WeeklyBbMidBandSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyBollingerMidBand),
                WeeklyBbLowerBandSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyBollingerLowerBand),
                WeeklyRsiSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyRSI14),
                WeeklyMacdLineSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyMACDLine),
                WeeklyMacdSignalSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyMACDSignal),
                WeeklyMacdHistogramSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyMACDHistogram),
                H4OpenSeries = BuildRecentH4CandleSeries(candles, scanIndex, x => x.Open),
                H4HighSeries = BuildRecentH4CandleSeries(candles, scanIndex, x => x.High),
                H4LowSeries = BuildRecentH4CandleSeries(candles, scanIndex, x => x.Low),
                H4CloseSeries = BuildRecentH4CandleSeries(candles, scanIndex, x => x.Close),
                H4BbUpperBandSeries = BuildRecentH4Series(candles, scanIndex, x => x.H4BollingerUpperBand),
                H4BbMidBandSeries = BuildRecentH4Series(candles, scanIndex, x => x.H4BollingerMidBand),
                H4BbLowerBandSeries = BuildRecentH4Series(candles, scanIndex, x => x.H4BollingerLowerBand),
                H4RsiSeries = BuildRecentH4Series(candles, scanIndex, x => x.RSI14),
                H4MacdLineSeries = BuildRecentH4Series(candles, scanIndex, x => x.MACDLine),
                H4MacdSignalSeries = BuildRecentH4Series(candles, scanIndex, x => x.MACDSignal),
                H4MacdHistogramSeries = BuildRecentH4Series(candles, scanIndex, x => x.MACDHistogram)
            };
        }

        private List<Candle>? TryLoadSessionAlignedH4Candles(string ticker, DateTime scanTime)
        {
            if (!_historicalCache.TryLoad(ticker, Timeframe.M15, out var m15) ||
                m15 == null ||
                m15.Count == 0)
            {
                return null;
            }

            var aligned = SessionAlignedH4Builder.Build(m15, scanTime);
            if (aligned.Count == 0)
                return null;

            _logger.Debug($"Session-aligned H4 view built: {ticker}, bars={aligned.Count}, " +
                          $"last={aligned[^1].Time:yyyy-MM-dd HH:mm:ss}");
            return aligned;
        }

        private RecentFeatureSeries? BuildReversalPatternSeries(List<Candle>? dailyCandles)
        {
            if (dailyCandles == null || dailyCandles.Count < 2)
                return null;

            var ordered = dailyCandles
                .OrderBy(x => x.Time)
                .ToList();
            var marketToday = MarketTime.Now().Date;
            var completed = ordered[^1].Time.Date == marketToday
                ? ordered.Take(ordered.Count - 1).ToList()
                : ordered;

            if (completed.Count < 2)
                return null;

            var scanIndex = completed.Count - 1;
            return new RecentFeatureSeries
            {
                DailyCloseSeries = [.. completed
                    .TakeLast(RecentDailySeriesLength)
                    .Select(x => decimal.Round(x.Close, 2, MidpointRounding.AwayFromZero))],
                DailyBbUpperBandSeries = BuildRecentDailySeries(
                    completed,
                    scanIndex,
                    x => x.DailyBollingerUpperBand),
                DailyBbMidBandSeries = BuildRecentDailySeries(
                    completed,
                    scanIndex,
                    x => x.DailyBollingerMidBand),
                DailyBbLowerBandSeries = BuildRecentDailySeries(
                    completed,
                    scanIndex,
                    x => x.DailyBollingerLowerBand),
                DailyRsiSeries = BuildRecentDailySeries(
                    completed,
                    scanIndex,
                    x => x.DailyRSI14),
                DailyMacdLineSeries = BuildRecentDailySeries(
                    completed,
                    scanIndex,
                    x => x.DailyMACDLine),
                DailyMacdSignalSeries = BuildRecentDailySeries(
                    completed,
                    scanIndex,
                    x => x.DailyMACDSignal),
                DailyMacdHistogramSeries = BuildRecentDailySeries(
                    completed,
                    scanIndex,
                    x => x.DailyMACDHistogram)
            };
        }

        private static bool HasMinimumReversalPatternRows(RecentFeatureSeries? series)
        {
            return series != null &&
                   series.DailyCloseSeries.Count >= 6 &&
                   series.DailyBbUpperBandSeries.Count >= 6 &&
                   series.DailyBbMidBandSeries.Count >= 6 &&
                   series.DailyBbLowerBandSeries.Count >= 6 &&
                   series.DailyRsiSeries.Count >= 4 &&
                   series.DailyMacdHistogramSeries.Count >= 4;
        }

        private static RecentFeatureSeries BuildH4ReversalPatternSeries(
            List<Candle> candles,
            RecentFeatureSeries scannerSeries)
        {
            var closes = candles
                .OrderBy(x => x.Time)
                .TakeLast(scannerSeries.H4BbMidBandSeries.Count)
                .Select(x => decimal.Round(x.Close, 2, MidpointRounding.AwayFromZero))
                .ToList();

            return new RecentFeatureSeries
            {
                DailyCloseSeries = closes,
                DailyBbUpperBandSeries = [.. scannerSeries.H4BbUpperBandSeries],
                DailyBbMidBandSeries = [.. scannerSeries.H4BbMidBandSeries],
                DailyBbLowerBandSeries = [.. scannerSeries.H4BbLowerBandSeries],
                DailyRsiSeries = [.. scannerSeries.H4RsiSeries],
                DailyMacdLineSeries = [.. scannerSeries.H4MacdLineSeries],
                DailyMacdSignalSeries = [.. scannerSeries.H4MacdSignalSeries],
                DailyMacdHistogramSeries = [.. scannerSeries.H4MacdHistogramSeries]
            };
        }

        private List<decimal> BuildRecentDailyCloseSeries(List<Candle> candles)
        {
            var dailyBars = BuildDailyBars(candles);
            if (dailyBars.Count == 0)
                return [];

            var completedDailyBars = TakeCompletedDailyBars(dailyBars);

            return [.. completedDailyBars
                .TakeLast(RecentDailySeriesLength)
                .Select(x => decimal.Round(x.Close, 2, MidpointRounding.AwayFromZero))];
        }

        private BollingerStateSet BuildBollingerStateSet(RecentFeatureSeries recentSeries)
        {
            return new BollingerStateSet
            {
                Weekly = AnalyzeBollingerState(
                    recentSeries.WeeklyBbUpperBandSeries,
                    recentSeries.WeeklyBbMidBandSeries,
                    recentSeries.WeeklyBbLowerBandSeries),
                Daily = AnalyzeBollingerState(
                    recentSeries.DailyBbUpperBandSeries,
                    recentSeries.DailyBbMidBandSeries,
                    recentSeries.DailyBbLowerBandSeries),
                H4 = AnalyzeBollingerState(
                    recentSeries.H4BbUpperBandSeries,
                    recentSeries.H4BbMidBandSeries,
                    recentSeries.H4BbLowerBandSeries)
            };
        }

        private BollingerStateOutput AnalyzeBollingerState(
            IReadOnlyList<decimal> upper,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> lower)
        {
            var count = Math.Min(upper.Count, Math.Min(mid.Count, lower.Count));
            var alignedUpper = upper.TakeLast(count).ToArray();
            var alignedMid = mid.TakeLast(count).ToArray();
            var alignedLower = lower.TakeLast(count).ToArray();

            return ToOutput(_bollingerFigureAnalyzer.Analyze(new BollingerFeatureSeries
            {
                UpperSeries = [.. alignedUpper],
                MidSeries = [.. alignedMid],
                LowerSeries = [.. alignedLower]
            }));
        }

        private static BollingerStateOutput ToOutput(BollingerFigureState state)
        {
            return new BollingerStateOutput
            {
                Direction = state.Direction.ToString(),
                Regime = state.Regime.ToString(),
                MidSlope = state.MidSlope,
                WidthSlope = state.WidthSlope,
                UpperDistanceSlope = state.UpperDistanceSlope
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
            return [.. indexes.Select(i => decimal.Round(selector(_featureEngine.Calculate(candles, i + 1)), 2, MidpointRounding.AwayFromZero))];
        }

        // Must drop the same still-forming last bar that BuildRecentDailyCloseSeries drops below -
        // otherwise Open/High/Low's "last" element is today's in-progress bar while Close's "last"
        // element is T-1's completed close, silently pairing two different calendar days together.
        // Found 2026-09-04 via CHPT: DailyOpenSeries[^1]=9.31 (today's open) was being paired with
        // DailyCloseSeries[^1]=9.07 (T-1's close) to compute a candle body, producing a nonsense
        // negative/tiny body instead of T-1's real +31% burst candle (O=6.90 -> C=9.08).
        private static List<decimal> BuildRecentDailyCandleSeries(
            List<Candle> candles,
            int scanIndex,
            Func<Candle, decimal> selector)
        {
            var dailyBars = BuildDailyBars(candles.Take(scanIndex + 1).ToList());
            var completedDailyBars = TakeCompletedDailyBars(dailyBars);
            return [.. completedDailyBars
                .TakeLast(RecentDailySeriesLength)
                .Select(x => decimal.Round(selector(x), 2, MidpointRounding.AwayFromZero))];
        }

        // Only drop the last daily bar when it actually IS today's still-forming bar - assuming it
        // always is (by position alone) breaks right after a market holiday: if today's first H4 bar
        // hasn't posted yet at scan time, the candle series' last entry is really the prior *completed*
        // session (e.g. Friday's close after a Monday holiday), and blindly dropping it silently loses
        // that whole day from every Daily-based series (close/open/high/low, and everything derived
        // from them - Bollinger regime, quality score, the burst/readiness checks). Found 2026-09-08 on
        // HAFN the morning after Labor Day: RecentDailyCloseSeries ended on Thursday's close, entirely
        // missing Friday's real (and, per the user's chart read, boost-sized) candle.
        private static List<Candle> TakeCompletedDailyBars(List<Candle> dailyBars)
        {
            if (dailyBars.Count <= 1)
                return dailyBars;

            return dailyBars[^1].Time.Date == MarketTime.Now().Date
                ? dailyBars.Take(dailyBars.Count - 1).ToList()
                : dailyBars;
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
                .Select(x => decimal.Round(x!.Value, 2, MidpointRounding.AwayFromZero))];
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
            return [.. indexes.Select(i => decimal.Round(selector(_featureEngine.Calculate(candles, i + 1)), 2, MidpointRounding.AwayFromZero))];
        }

        private static List<decimal> BuildRecentH4CandleSeries(
            List<Candle> candles,
            int scanIndex,
            Func<Candle, decimal> selector)
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
            return [.. indexes.Select(i => decimal.Round(selector(candles[i]), 2, MidpointRounding.AwayFromZero))];
        }

        private bool TryLoadPreparedH4CandlesFromCache(
            string ticker,
            FinderSettings finderSettings,
            out List<Candle>? candles)
        {
            candles = null;

            if (!_historicalCache.TryLoad(ticker, Timeframe.H4, out var cached) ||
                cached == null ||
                cached.Count < finderSettings.MinimumCandles)
            {
                return false;
            }

            candles = PrepareFinderCandles(ticker, cached, finderSettings);
            if (candles == null || candles.Count < finderSettings.MinimumCandles)
                return false;

            var dailyBars = BuildDailyBars(candles);
            var marketNow = MarketTime.Now();
            var expectedLatestClosedDailyDate = GetExpectedLatestClosedDailyDate(marketNow.Date);
            var latestDailyDate = dailyBars.Count > 0
                ? dailyBars[^1].Time.Date
                : DateTime.MinValue;

            if (latestDailyDate < expectedLatestClosedDailyDate)
            {
                _logger.Info(
                    $"Historical cache stale for scanner: {ticker}. " +
                    $"LatestDailyDate={latestDailyDate:yyyy-MM-dd}, " +
                    $"ExpectedLatestClosedDailyDate={expectedLatestClosedDailyDate:yyyy-MM-dd}. " +
                    "Reloading history.");
                return false;
            }

            // "Latest closed daily bar is yesterday" only proves the cache is fresh once trading
            // for today is done. If today's session (pre-market onward) has already started and the
            // cache still has no bar dated today, today's forming H4 bars (pre-market/regular-hours
            // burst included) are missing - exactly what let a same-day BellUp burst slip past the
            // phase-readiness check. Reload instead of trusting a daily-only staleness signal.
            var todaysSessionHasStarted =
                marketNow.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
                marketNow.TimeOfDay >= PreMarketSessionStart;

            if (todaysSessionHasStarted && latestDailyDate < marketNow.Date)
            {
                _logger.Info(
                    $"Historical cache missing today's session for scanner: {ticker}. " +
                    $"LatestDailyDate={latestDailyDate:yyyy-MM-dd}, MarketToday={marketNow.Date:yyyy-MM-dd}. " +
                    "Reloading history.");
                return false;
            }

            _logger.Info(
                $"Historical cache used for scanner: {ticker}. " +
                $"H4={candles.Count}, LookbackDays={finderSettings.LookbackCalendarDays}");

            return true;
        }

        private List<Candle>? PrepareFinderCandles(
            string ticker,
            List<Candle>? candles,
            FinderSettings finderSettings)
        {
            if (candles == null || candles.Count == 0)
                return candles;

            var prepared = candles;

            if (finderSettings.CandleCount > 0 &&
                prepared.Count > finderSettings.CandleCount)
            {
                var trimmedCandles = prepared
                    .TakeLast(finderSettings.CandleCount)
                    .ToList();

                var trimmedWeeklyBars = BuildWeeklyBars(trimmedCandles);

                if (trimmedWeeklyBars.Count >= 20)
                {
                    prepared = trimmedCandles;

                    _logger.Info(
                        $"Ticker history trimmed: {ticker}. " +
                        $"H4={prepared.Count}, W1={trimmedWeeklyBars.Count}");
                }
                else
                {
                    _logger.Info(
                        $"Ticker history trim skipped: {ticker}. " +
                        $"RequestedH4={finderSettings.CandleCount}, " +
                        $"TrimmedW1={trimmedWeeklyBars.Count} is too short for weekly analysis.");
                }
            }

            return prepared;
        }

        private static bool HasPositiveSlope(List<decimal> series, decimal minSlope)
            => series.Count >= 2 && series[^1] - series[0] >= minSlope;

        private void LogScanPerformanceSummary(ScanPerformanceSummary performance)
        {
            if (performance.Stages.Count == 0)
                return;

            _logger.Info("Scan performance summary:");
            _logger.Info("Stage                 Input  Proc  Unique  HistLoads  Cache  Added  Skipped  Elapsed");

            foreach (var stage in performance.Stages)
            {
                _logger.Info(
                    $"{TrimOrPad(stage.Name, 21)}" +
                    $"{stage.Input,6}" +
                    $"{stage.Processed,6}" +
                    $"{stage.UniqueTickers.Count,8}" +
                    $"{stage.HistoricalLoads,11}" +
                    $"{stage.CacheHits,7}" +
                    $"{stage.Added,7}" +
                    $"{stage.Skipped,9}" +
                    $"{ElapsedTimeFormatter.Format(stage.Elapsed),9}");
            }

            var totalInput = performance.Stages.Sum(x => x.Input);
            var totalProcessed = performance.Stages.Sum(x => x.Processed);
            var totalUnique = performance.Stages
                .SelectMany(x => x.UniqueTickers)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var totalHistoricalLoads = performance.Stages.Sum(x => x.HistoricalLoads);
            var totalCacheHits = performance.Stages.Sum(x => x.CacheHits);
            var totalAdded = performance.Stages.Sum(x => x.Added);
            var totalSkipped = performance.Stages.Sum(x => x.Skipped);
            var totalElapsed = TimeSpan.FromTicks(performance.Stages.Sum(x => x.Elapsed.Ticks));

            _logger.Info(
                $"{TrimOrPad("Total", 21)}" +
                $"{totalInput,6}" +
                $"{totalProcessed,6}" +
                $"{totalUnique,8}" +
                $"{totalHistoricalLoads,11}" +
                $"{totalCacheHits,7}" +
                $"{totalAdded,7}" +
                $"{totalSkipped,9}" +
                $"{ElapsedTimeFormatter.Format(totalElapsed),9}");

            var slowest = performance.Tickers
                .Where(x => x.Elapsed > TimeSpan.Zero)
                .OrderByDescending(x => x.Elapsed)
                .Take(10)
                .ToList();

            if (slowest.Count == 0)
                return;

            _logger.Info("Slowest ticker operations:");
            _logger.Info("Stage                 Ticker  HistLoads  Cache  Added  Skipped  Errors  Elapsed");

            foreach (var ticker in slowest)
            {
                _logger.Info(
                    $"{TrimOrPad(ticker.StageName, 21)}" +
                    $"{TrimOrPad(ticker.Ticker, 8)}" +
                    $"{ticker.HistoricalLoads,11}" +
                    $"{ticker.CacheHits,7}" +
                    $"{BoolFlag(ticker.Added),7}" +
                    $"{BoolFlag(ticker.Skipped),9}" +
                    $"{ticker.Errors,8}" +
                    $"{ElapsedTimeFormatter.Format(ticker.Elapsed),9}");
            }
        }

        private static string TrimOrPad(string value, int width)
        {
            if (value.Length > width)
                return value[..width];

            return value.PadRight(width);
        }

        private static string BoolFlag(bool value)
            => value ? "Y" : "";

        private static bool HasPositiveRelativeSlope(List<decimal> series, decimal minSlopePct)
            => CalculateRelativeSlopePct(series) >= minSlopePct;

        private static int CountUpMoves(List<decimal> series)
            => series.Count < 2 ? 0 : series.Zip(series.Skip(1), (a, b) => b > a ? 1 : 0).Sum();

        private static bool IsRollingOver(List<decimal> series)
            => series.Count >= 3 && series[^1] < series[^2] && series[^2] <= series[^3];

        private static bool IsExhausted(List<decimal> series, decimal threshold)
            => series.Count >= 3 &&
               series[^1] >= threshold &&
               series[^1] <= series[^2];

        private static decimal CalculateSlope(List<decimal> series)
            => BellPatternClassifier.CalculateSlope(series);

        private static decimal CalculateRelativeSlopePct(List<decimal> series)
            => BellPatternClassifier.CalculateRelativeSlopePct(series);

        private static decimal CalculateTailRelativeSlopePct(List<decimal> series, int lookback)
            => BellPatternClassifier.CalculateTailRelativeSlopePct(series, lookback);

        private static decimal CalculateTailBandWidthDeltaPct(
            List<decimal> upper,
            List<decimal> mid,
            List<decimal> lower,
            int lookback)
        {
            var count = Math.Min(upper.Count, Math.Min(mid.Count, lower.Count));
            if (count < 2)
                return 0m;

            var widths = upper
                .TakeLast(count)
                .Zip(mid.TakeLast(count), (u, m) => new { Upper = u, Mid = m })
                .Zip(lower.TakeLast(count), (x, l) =>
                    x.Mid == 0m
                        ? 0m
                        : (x.Upper - l) / Math.Abs(x.Mid) * 100m)
                .ToList();

            return CalculateTailSlope(widths, lookback);
        }

        private static decimal CalculateTailSlope(List<decimal> series, int lookback)
            => BellPatternClassifier.CalculateTailSlope(series, lookback);

        private static decimal CalculateSegmentRelativeSlopePct(
            List<decimal> series,
            int lookback,
            int offsetFromEnd)
            => BellPatternClassifier.CalculateSegmentRelativeSlopePct(series, lookback, offsetFromEnd);

        private static bool IsBullishBandKink(
            decimal priorSlopePct,
            decimal recentSlopePct)
            => recentSlopePct > priorSlopePct;

        private static bool IsStrictTodayResearchLikeRunawayPattern(
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries)
        {
            var dailyUpperRecent = CalculateTailRelativeSlopePct(recentSeries.DailyBbUpperBandSeries, 6);
            var dailyUpperPrior = CalculateSegmentRelativeSlopePct(recentSeries.DailyBbUpperBandSeries, 6, 6);
            var dailyMidRecent = CalculateTailRelativeSlopePct(recentSeries.DailyBbMidBandSeries, 6);
            var dailyMidPrior = CalculateSegmentRelativeSlopePct(recentSeries.DailyBbMidBandSeries, 6, 6);
            var dailyLowerRecent = CalculateTailRelativeSlopePct(recentSeries.DailyBbLowerBandSeries, 6);
            var dailyLowerPrior = CalculateSegmentRelativeSlopePct(recentSeries.DailyBbLowerBandSeries, 6, 6);

            var h4UpperRecent = CalculateTailRelativeSlopePct(recentSeries.H4BbUpperBandSeries, 4);
            var h4UpperPrior = CalculateSegmentRelativeSlopePct(recentSeries.H4BbUpperBandSeries, 4, 4);
            var h4MidRecent = CalculateTailRelativeSlopePct(recentSeries.H4BbMidBandSeries, 4);
            var h4MidPrior = CalculateSegmentRelativeSlopePct(recentSeries.H4BbMidBandSeries, 4, 4);
            var h4LowerRecent = CalculateTailRelativeSlopePct(recentSeries.H4BbLowerBandSeries, 4);
            var h4LowerPrior = CalculateSegmentRelativeSlopePct(recentSeries.H4BbLowerBandSeries, 4, 4);

            var weeklyUpperSlope = CalculateRelativeSlopePct(recentSeries.WeeklyBbUpperBandSeries);
            var weeklyMidSlope = CalculateRelativeSlopePct(recentSeries.WeeklyBbMidBandSeries);
            var weeklyLowerSlope = CalculateRelativeSlopePct(recentSeries.WeeklyBbLowerBandSeries);
            var weeklyDirectionUp = bbState.Weekly.Direction == nameof(BollingerFigureDirection.Up);
            var weeklyRegimeConstructive =
                bbState.Weekly.Regime == nameof(BollingerFigureRegime.Runaway) ||
                bbState.Weekly.Regime == nameof(BollingerFigureRegime.Pullback) ||
                bbState.Weekly.Regime == nameof(BollingerFigureRegime.Reacceleration);
            var dailyDirectionUp = bbState.Daily.Direction == nameof(BollingerFigureDirection.Up);
            var dailyRegimeConstructive =
                bbState.Daily.Regime == nameof(BollingerFigureRegime.Runaway) ||
                bbState.Daily.Regime == nameof(BollingerFigureRegime.Pullback) ||
                bbState.Daily.Regime == nameof(BollingerFigureRegime.Reacceleration);
            var h4DirectionUp = bbState.H4.Direction == nameof(BollingerFigureDirection.Up);
            var h4RegimeConstructive =
                bbState.H4.Regime == nameof(BollingerFigureRegime.Runaway) ||
                bbState.H4.Regime == nameof(BollingerFigureRegime.Reacceleration) ||
                bbState.H4.Regime == nameof(BollingerFigureRegime.Pullback);

            var weeklyConstructive =
                weeklyDirectionUp &&
                weeklyRegimeConstructive &&
                weeklyUpperSlope >= weeklyMidSlope &&
                weeklyMidSlope >= weeklyLowerSlope;

            var dailyRunawayKink =
                dailyDirectionUp &&
                dailyRegimeConstructive &&
                IsBullishBandKink(dailyUpperPrior, dailyUpperRecent) &&
                IsBullishBandKink(dailyMidPrior, dailyMidRecent) &&
                IsBullishBandKink(dailyLowerPrior, dailyLowerRecent);

            var h4RunawayKink =
                h4DirectionUp &&
                h4RegimeConstructive &&
                IsBullishBandKink(h4UpperPrior, h4UpperRecent) &&
                IsBullishBandKink(h4MidPrior, h4MidRecent) &&
                IsBullishBandKink(h4LowerPrior, h4LowerRecent);

            return weeklyConstructive && dailyRunawayKink && h4RunawayKink;
        }

        private static bool IsReversalHookPattern(
            RecentFeatureSeries recentSeries,
            out string diagnostics)
        {
            var upper = recentSeries.DailyBbUpperBandSeries;
            var mid = recentSeries.DailyBbMidBandSeries;
            var lower = recentSeries.DailyBbLowerBandSeries;
            var macdHistogram = recentSeries.DailyMacdHistogramSeries;

            var result = BellPatternClassifier.IsReversalHookPattern(
                recentSeries.DailyCloseSeries,
                upper,
                mid,
                lower,
                recentSeries.DailyRsiSeries,
                recentSeries.DailyMacdLineSeries,
                recentSeries.DailyMacdSignalSeries,
                macdHistogram,
                out diagnostics);

            if (upper.Count >= 6 && mid.Count >= 6 && lower.Count >= 6 && macdHistogram.Count >= 4)
            {
                diagnostics +=
                    $", LowerDeltasTail={FormatTail(CalculateDeltas(lower), 4)}, " +
                    $"MidDeltasTail={FormatTail(CalculateDeltas(mid), 4)}, " +
                    $"MacdHistogramTail={FormatTail(macdHistogram, 4)}";
            }

            return result;
        }

        private static bool IsPriceTurningTowardDailyMid(RecentFeatureSeries recentSeries)
            => BellPatternClassifier.IsPriceTurningTowardDailyMid(
                recentSeries.DailyCloseSeries,
                recentSeries.DailyBbMidBandSeries);

        private static bool IsRealBollingerLaunch(
            decimal midSlopePct,
            decimal upperSlopePct,
            decimal lowerSlopePct)
        {
            return midSlopePct > 0m &&
                   upperSlopePct > midSlopePct &&
                   lowerSlopePct <= midSlopePct;
        }

        private static List<decimal> CalculateDeltas(List<decimal> series)
            => BellPatternClassifier.CalculateDeltas(series);

        private static string FormatTail(List<decimal> series, int count)
        {
            if (series.Count == 0)
                return "[]";

            return "[" + string.Join(" ", series.TakeLast(count).Select(x => x.ToString("G29", CultureInfo.InvariantCulture))) + "]";
        }

        private static decimal Positive(decimal value)
            => value > 0m ? value : 0m;

        private static decimal Negative(decimal value)
            => value < 0m ? -value : 0m;

        private static decimal Closeness(decimal actual, decimal target, decimal tolerance)
        {
            if (tolerance <= 0m)
                return 0m;

            return Positive(1m - Math.Abs(actual - target) / tolerance);
        }

        private static bool IsResearchLikeLaunch(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            RecentFeatureSeries recentSeries,
            NextDayRankingSettings settings)
        {
            return snapshot.Current.DistanceTo20dHigh <= settings.ResearchLikeDistanceTo20dHighThreshold &&
                   snapshot.Current.DailyRSI14 <= settings.ResearchLikeMaxDailyRsi14 &&
                   diagnostics.ATRRatio >= settings.ResearchLikeMinAtrRatio &&
                   diagnostics.TrendPosition <= settings.ResearchLikeMaxTrendPosition &&
                   diagnostics.BBMidSignedDistancePct <= settings.ResearchLikeMaxBbMid &&
                   HasPositiveSlope(recentSeries.DailyRsiSeries, settings.PatternDailyRsiSlopeThreshold) &&
                   HasPositiveRelativeSlope(recentSeries.H4BbMidBandSeries, settings.PatternH4MaSlopeThreshold);
        }

        private static bool IsResearchLikeReadyNow(
            RecentFeatureSeries recentSeries,
            ResearchLikeExitSettings settings)
        {
            return settings.Enabled &&
                   CalculateSlope(recentSeries.DailyRsiSeries) >= settings.MinDailyRsiSlope &&
                   CalculateSlope(recentSeries.DailyMacdHistogramSeries) >= settings.MinDailyMacdSlope &&
                   CalculateSlope(recentSeries.H4RsiSeries) >= settings.MinH4RsiSlope &&
                   CalculateSlope(recentSeries.H4MacdHistogramSeries) >= settings.MinH4MacdSlope &&
                   CountUpMoves(recentSeries.H4RsiSeries) >= settings.MinH4RsiUpMoves;
        }

        private static bool ShouldRejectByWeeklyBbForWishlist(BollingerStateOutput weekly)
        {
            return weekly.Direction == nameof(BollingerFigureDirection.Down) &&
                   (weekly.Regime is nameof(BollingerFigureRegime.Collapse) or
                    nameof(BollingerFigureRegime.Runaway));
        }

        private bool ShouldBypassAgedWeeklyBbVeto(
            WishListItem item,
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            decimal entryScore)
        {
            if (!IsLiveMoverPreset(item.Scan.PresetScanCode))
                return false;

            if (snapshot.Current.DistanceTo20dHigh > -0.25m)
                return false;

            if (ShouldBypassWishListFilterForLiveScan(snapshot, diagnostics, entryScore))
                return true;

            var recoveredSeries =
                snapshot.Current.DailyMaSignedDistancePct > -35m &&
                snapshot.Current.DailyRSI14 >= 35m &&
                snapshot.DailyRsiDelta3 >= -5m &&
                snapshot.H4MaDelta3 >= -14m &&
                diagnostics.ATRRatio >= 3m;

            var strongEnough =
                diagnostics.ATRRatio >= 4m ||
                snapshot.Current.DailyRSI14 >= 60m ||
                entryScore >= 20m ||
                item.Score.Score >= 20m;

            return recoveredSeries && strongEnough;
        }

        private static bool ShouldAllowWeeklyBbWishlistBypass(BollingerStateOutput weekly)
        {
            return weekly.Direction == nameof(BollingerFigureDirection.Up) &&
                   (weekly.Regime is nameof(BollingerFigureRegime.Runaway) or
                    nameof(BollingerFigureRegime.Pullback) or
                    nameof(BollingerFigureRegime.Reacceleration));
        }

        private static decimal CalculateBbRankAdjustment(
            BollingerStateOutput weekly,
            BollingerStateOutput daily,
            BollingerStateOutput h4)
        {
            decimal score = 0m;
            var isBullishMinFirstSetup = IsBullishMinFirstSetup(
                weekly.Regime,
                weekly.Direction,
                daily.Regime,
                daily.Direction,
                h4.Regime,
                h4.Direction);

            score += CalculateBbTierAdjustment(weekly.Regime, weekly.Direction, 0.06m, 0.03m, 0.12m, 0.08m);
            score += CalculateBbTierAdjustment(daily.Regime, daily.Direction, 0.16m, 0.06m, 0.20m, 0.10m);
            score += isBullishMinFirstSetup
                ? 0.01m
                : CalculateBbTierAdjustment(h4.Regime, h4.Direction, 0.10m, 0.02m, 0.14m, 0.10m);

            if (daily.Direction == nameof(BollingerFigureDirection.Up) &&
                daily.Regime == nameof(BollingerFigureRegime.Runaway) &&
                h4.Direction == nameof(BollingerFigureDirection.Up) &&
                h4.Regime == nameof(BollingerFigureRegime.Runaway))
            {
                score += 0.08m;
            }

            if (weekly.Direction == nameof(BollingerFigureDirection.Up) &&
                daily.Direction == nameof(BollingerFigureDirection.Up) &&
                daily.Regime == nameof(BollingerFigureRegime.Pullback) &&
                h4.Direction == nameof(BollingerFigureDirection.Up) &&
                h4.Regime == nameof(BollingerFigureRegime.Pullback))
            {
                score -= 0.04m;
            }

            if (daily.Direction == nameof(BollingerFigureDirection.Down) &&
                daily.Regime == nameof(BollingerFigureRegime.Collapse) &&
                h4.Direction == nameof(BollingerFigureDirection.Down))
            {
                score -= 0.10m;
            }

            if (isBullishMinFirstSetup)
                score += 0.06m;

            return score;
        }

        private static decimal CalculateBbSecondPassAdjustment(
            string weeklyRegime,
            string weeklyDirection,
            string dailyRegime,
            string dailyDirection,
            string h4Regime,
            string h4Direction)
        {
            var isBullishMinFirstSetup = IsBullishMinFirstSetup(
                weeklyRegime,
                weeklyDirection,
                dailyRegime,
                dailyDirection,
                h4Regime,
                h4Direction);

            return
                CalculateBbTierAdjustment(weeklyRegime, weeklyDirection, 0.03m, 0.02m, 0.06m, 0.04m) +
                CalculateBbTierAdjustment(dailyRegime, dailyDirection, 0.10m, 0.03m, 0.12m, 0.06m) +
                (isBullishMinFirstSetup
                    ? 0.02m
                    : CalculateBbTierAdjustment(h4Regime, h4Direction, 0.05m, 0.01m, 0.08m, 0.06m)) +
                (isBullishMinFirstSetup ? 0.04m : 0m);
        }

        private static decimal CalculateBbTierAdjustment(
            string regime,
            string direction,
            decimal runawayBonus,
            decimal pullbackBonus,
            decimal downwardPenalty,
            decimal collapsePenalty)
        {
            var isUp = direction == nameof(BollingerFigureDirection.Up);
            var isDown = direction == nameof(BollingerFigureDirection.Down);

            return regime switch
            {
                nameof(BollingerFigureRegime.Runaway) when isUp => runawayBonus,
                nameof(BollingerFigureRegime.Reacceleration) when isUp => runawayBonus * 0.85m,
                nameof(BollingerFigureRegime.Pullback) when isUp => pullbackBonus,
                nameof(BollingerFigureRegime.Runaway) when isDown => -downwardPenalty,
                nameof(BollingerFigureRegime.Collapse) when isDown => -collapsePenalty,
                nameof(BollingerFigureRegime.Collapse) when isUp => -collapsePenalty * 0.60m,
                nameof(BollingerFigureRegime.Pullback) when isDown => -pullbackBonus,
                _ => 0m
            };
        }

        private static decimal? ResolveBollingerEntryDiscountOverridePct(
            BollingerStateSet bbState,
            decimal? currentEntryDiscountPct)
        {
            var adjusted = currentEntryDiscountPct;
            var isBullishMinFirstSetup = IsBullishMinFirstSetup(
                bbState.Weekly.Regime,
                bbState.Weekly.Direction,
                bbState.Daily.Regime,
                bbState.Daily.Direction,
                bbState.H4.Regime,
                bbState.H4.Direction);

            if (bbState.H4.Direction == nameof(BollingerFigureDirection.Up) &&
                bbState.H4.Regime == nameof(BollingerFigureRegime.Pullback))
            {
                adjusted = MaxDiscount(adjusted, 0.02m);
            }

            if (bbState.H4.Regime == nameof(BollingerFigureRegime.Collapse))
            {
                adjusted = MaxDiscount(
                    adjusted,
                    bbState.H4.Direction == nameof(BollingerFigureDirection.Down) ? 0.035m : 0.025m);
            }

            if (bbState.Daily.Direction == nameof(BollingerFigureDirection.Up) &&
                bbState.Daily.Regime == nameof(BollingerFigureRegime.Pullback) &&
                bbState.H4.Direction == nameof(BollingerFigureDirection.Up) &&
                bbState.H4.Regime == nameof(BollingerFigureRegime.Pullback))
            {
                adjusted = MaxDiscount(adjusted, 0.03m);
            }

            if (bbState.Daily.Direction == nameof(BollingerFigureDirection.Down) &&
                bbState.Daily.Regime == nameof(BollingerFigureRegime.Collapse))
            {
                adjusted = MaxDiscount(adjusted, 0.035m);
            }

            if (isBullishMinFirstSetup)
            {
                adjusted = MaxDiscount(
                    adjusted,
                    bbState.Daily.Regime == nameof(BollingerFigureRegime.Collapse) ? 0.035m : 0.03m);
            }

            return adjusted;
        }

        private static SeriesEntryProfileDecision ResolveSeriesEntryProfileDiscountOverridePct(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            RecentFeatureSeries recentSeries,
            BollingerStateSet bbState,
            SeriesEntryProfileSettings settings,
            decimal? currentEntryDiscountPct)
        {
            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            var dailyUpperSlope = CalculateRelativeSlopePct(recentSeries.DailyBbUpperBandSeries);
            var dailyLowerSlope = CalculateRelativeSlopePct(recentSeries.DailyBbLowerBandSeries);
            var dailyMaSlope = dailyMidSlope;
            var dailyRsiSlope = CalculateSlope(recentSeries.DailyRsiSeries);
            var dailyMacdHistogramSeries = recentSeries.DailyMacdHistogramSeries;
            var h4MacdHistogramSeries = recentSeries.H4MacdHistogramSeries;
            var dailyMacdSlope = CalculateSlope(dailyMacdHistogramSeries);
            var dailyMacdLineSlope = CalculateSlope(recentSeries.DailyMacdLineSeries);
            var dailyMacdSignalSlope = CalculateSlope(recentSeries.DailyMacdSignalSeries);
            var h4MidSlope = CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries);
            var h4UpperSlope = CalculateRelativeSlopePct(recentSeries.H4BbUpperBandSeries);
            var h4LowerSlope = CalculateRelativeSlopePct(recentSeries.H4BbLowerBandSeries);
            var h4MaSlope = h4MidSlope;
            var h4RsiSlope = CalculateSlope(recentSeries.H4RsiSeries);
            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdHistogramSeries);
            var h4MacdLineSlope = CalculateSlope(recentSeries.H4MacdLineSeries);
            var h4MacdSignalSlope = CalculateSlope(recentSeries.H4MacdSignalSeries);
            var dailyRsiLast = GetLatestValue(recentSeries.DailyRsiSeries);
            var h4RsiLast = GetLatestValue(recentSeries.H4RsiSeries);
            var dailyMacdLast = GetLatestValue(dailyMacdHistogramSeries);
            var h4MacdLast = GetLatestValue(h4MacdHistogramSeries);
            var todayResearchLikePatternKind = ClassifyTodayResearchLikePatternKind(bbState, recentSeries);
            var bellPatternSignal = ClassifyBellPatternSignal(bbState, recentSeries);
            var bellPatternKind = bellPatternSignal.Kind;
            var realDailyBbLaunch = IsRealBollingerLaunch(dailyMidSlope, dailyUpperSlope, dailyLowerSlope);
            var realH4BbLaunch = IsRealBollingerLaunch(h4MidSlope, h4UpperSlope, h4LowerSlope);
            var realDailyMacdConstructive =
                dailyMacdSlope >= 0m &&
                dailyMacdLineSlope >= dailyMacdSignalSlope &&
                dailyMacdLast >= 0m;
            var realH4MacdConstructive =
                h4MacdSlope >= 0m &&
                h4MacdLineSlope >= h4MacdSignalSlope &&
                h4MacdLast >= -0.05m;

            if (!settings.Enabled)
            {
                return new SeriesEntryProfileDecision(
                    currentEntryDiscountPct,
                    "Disabled",
                    dailyMidSlope,
                    dailyRsiSlope,
                    dailyMacdSlope,
                    h4MidSlope,
                    h4RsiSlope,
                    h4MacdSlope);
            }

            var dailyStrong =
                realDailyBbLaunch ||
                dailyMidSlope >= settings.DailyStrongSlopeThreshold ||
                dailyRsiSlope >= settings.DailyRsiSlopeThreshold ||
                snapshot.Current.DailyRSI14 >= settings.DailyOverheatedRsiThreshold;

            var h4Weakening =
                (!realH4BbLaunch && h4MidSlope <= settings.H4WeakSlopeThreshold) ||
                h4RsiSlope < 0m ||
                h4MacdSlope <= settings.H4MacdWeakDeltaThreshold ||
                bbState.H4.Direction == nameof(BollingerFigureDirection.Down) ||
                bbState.H4.Regime == nameof(BollingerFigureRegime.Collapse);

            var constructiveH4 =
                (realH4BbLaunch || h4MidSlope > 0m) &&
                h4RsiSlope > 0m &&
                (realH4MacdConstructive || h4MacdSlope > 0m) &&
                bbState.H4.Direction == nameof(BollingerFigureDirection.Up) &&
                bbState.H4.Regime is nameof(BollingerFigureRegime.Runaway) or
                                     nameof(BollingerFigureRegime.Reacceleration);

            var cleanContinuation =
                dailyStrong &&
                !h4Weakening &&
                h4MidSlope >= settings.H4ContinuationMidSlopeThreshold &&
                h4RsiSlope >= settings.H4ContinuationRsiSlopeThreshold &&
                h4MacdSlope >= settings.H4ContinuationMacdSlopeThreshold &&
                dailyMacdLast >= 0m &&
                h4MacdLast >= 0m &&
                bbState.Daily.Direction == nameof(BollingerFigureDirection.Up) &&
                bbState.Daily.Regime is nameof(BollingerFigureRegime.Runaway) or
                                      nameof(BollingerFigureRegime.Pullback) or
                                      nameof(BollingerFigureRegime.Collapse) or
                                      nameof(BollingerFigureRegime.Reacceleration);

            var overheated =
                snapshot.Current.DailyRSI14 >= settings.DailyOverheatedRsiThreshold ||
                h4RsiLast >= settings.H4OverheatedRsiThreshold;

            var nearHigh = snapshot.Current.DistanceTo20dHigh >= settings.NearHighDistanceTo20dHighThreshold;
            var highAtr = diagnostics.ATRRatio >= settings.MinAtrRatioForDeepEntry;
            var lateSpike =
                nearHigh &&
                (dailyRsiLast >= settings.LateSpikeDailyRsiThreshold ||
                 h4RsiLast >= settings.LateSpikeH4RsiThreshold) &&
                (dailyMaSlope >= settings.LateSpikeDailyMaSlopeThreshold ||
                 h4MaSlope >= settings.LateSpikeH4MaSlopeThreshold ||
                 dailyUpperSlope >= settings.LateSpikeDailyMaSlopeThreshold ||
                 h4UpperSlope >= settings.LateSpikeH4MaSlopeThreshold) &&
                (h4Weakening ||
                 h4MacdSlope <= 0m ||
                 IsRollingOver(recentSeries.H4RsiSeries));

            var immediateContinuation =
                !lateSpike &&
                dailyStrong &&
                constructiveH4 &&
                diagnostics.ATRRatio >= settings.ImmediateContinuationMinAtrRatio &&
                h4RsiLast >= settings.ImmediateContinuationMinH4Rsi &&
                dailyMacdLast >= 0m &&
                h4MacdLast >= 0m;

            var h4AdverseMomentum =
                h4MidSlope <= settings.DeepAdverseContinuationMaxH4MidSlopePct ||
                h4RsiSlope <= settings.DeepAdverseContinuationMaxH4RsiSlope ||
                h4MacdSlope <= settings.DeepAdverseContinuationMaxH4MacdSlope;

            var deepAdverseContinuation =
                !lateSpike &&
                dailyStrong &&
                diagnostics.ATRRatio >= settings.DeepAdverseContinuationMinAtrRatio &&
                (realDailyBbLaunch ||
                 dailyMidSlope >= settings.DailyStrongSlopeThreshold ||
                 snapshot.Current.DailyRSI14 >= settings.DailyOverheatedRsiThreshold) &&
                h4AdverseMomentum &&
                (bbState.H4.Direction == nameof(BollingerFigureDirection.Down) ||
                 bbState.H4.Regime == nameof(BollingerFigureRegime.Neutral) ||
                 bbState.H4.Regime == nameof(BollingerFigureRegime.Collapse));

            var fastContinuationShallow =
                !lateSpike &&
                !deepAdverseContinuation &&
                ((realDailyBbLaunch && realH4BbLaunch && realDailyMacdConstructive && realH4MacdConstructive) ||
                 (realH4BbLaunch && realH4MacdConstructive && dailyStrong) ||
                 cleanContinuation ||
                 (dailyStrong && constructiveH4) ||
                 (dailyMaSlope >= settings.LaunchContinuationMinDailyMaSlopePct &&
                  h4RsiLast >= settings.LaunchContinuationMinH4Rsi &&
                  h4MacdLast >= settings.LaunchContinuationMinH4Macd &&
                  !h4Weakening) ||
                 (dailyRsiLast >= 55m &&
                  h4RsiLast >= 50m &&
                  dailyMacdLast >= 0m &&
                  h4MacdLast >= -0.05m &&
                  !h4Weakening));

            var moderatePullback =
                !lateSpike &&
                !deepAdverseContinuation &&
                !fastContinuationShallow &&
                dailyRsiLast >= 45m &&
                h4RsiLast >= 42m &&
                h4MacdLast >= -0.35m;

            var deepPullback =
                !lateSpike &&
                !deepAdverseContinuation &&
                !fastContinuationShallow &&
                !moderatePullback &&
                (highAtr || h4Weakening || dailyStrong);

            var targetDiscountPct = currentEntryDiscountPct;
            var profile = "None";

            if (lateSpike)
            {
                targetDiscountPct = MaxDiscount(
                    currentEntryDiscountPct,
                    Math.Min(settings.LateSpikeAvoidDiscountPct, settings.MaxDiscountPct));
                profile = "AvoidLateSpike";
            }
            else if (deepAdverseContinuation)
            {
                targetDiscountPct = MaxDiscount(
                    currentEntryDiscountPct,
                    Math.Min(settings.DeepAdverseContinuationDiscountPct, settings.MaxDiscountPct));
                profile = "DeepAdverseContinuation";
            }
            else if (todayResearchLikePatternKind == TodayResearchLikePatternKind.BellUp)
            {
                var bellCap = bellPatternSignal.Timeframe switch
                {
                    BellPatternTimeframe.H4 => settings.ImmediateContinuationMaxDiscountPct,
                    BellPatternTimeframe.Daily => settings.ShallowContinuationMaxDiscountPct,
                    _ => settings.ImmediateContinuationMaxDiscountPct
                };

                targetDiscountPct = CapDiscount(
                    currentEntryDiscountPct,
                    Math.Min(bellCap, settings.MaxDiscountPct));
                profile = $"BellUp-{bellPatternSignal.Timeframe}";
            }
            else if (immediateContinuation)
            {
                targetDiscountPct = CapDiscount(
                    currentEntryDiscountPct,
                    Math.Min(settings.ImmediateContinuationMaxDiscountPct, settings.MaxDiscountPct));
                profile = "ImmediateContinuation";
            }
            else if (bellPatternKind == BellPatternKind.BellDown && IsBelowPreviousClosedDailyMid(recentSeries))
            {
                targetDiscountPct = MaxDiscount(
                    currentEntryDiscountPct,
                    Math.Min(settings.DeepPullbackDiscountPct, settings.MaxDiscountPct));
                profile = "BellDown";
            }
            else if (fastContinuationShallow)
            {
                targetDiscountPct = CapDiscount(
                    currentEntryDiscountPct,
                    Math.Min(
                        Math.Min(settings.ShallowContinuationMaxDiscountPct, settings.FastContinuationMaxDiscountPct),
                        settings.MaxDiscountPct));
                profile = "FastContinuationShallow";
            }
            else if (moderatePullback)
            {
                targetDiscountPct = CapDiscount(
                    currentEntryDiscountPct,
                    Math.Min(settings.ModeratePullbackMaxDiscountPct, settings.MaxDiscountPct));
                profile = "ModeratePullback";
            }
            else if (deepPullback)
            {
                targetDiscountPct = MaxDiscount(
                    currentEntryDiscountPct,
                    Math.Min(settings.DeepPullbackDiscountPct, settings.MaxDiscountPct));
                profile = "DeepPullback";
            }
            else if (dailyStrong && h4Weakening && highAtr)
            {
                targetDiscountPct = MaxDiscount(
                    currentEntryDiscountPct,
                    Math.Min(settings.LossLikeAdverseMoveDiscountPct, settings.MaxDiscountPct));
                profile = "WaitPullback";
            }
            else if (dailyStrong && h4Weakening)
            {
                targetDiscountPct = MaxDiscount(
                    currentEntryDiscountPct,
                    Math.Min(settings.WaitPullbackDiscountPct, settings.MaxDiscountPct));
                profile = "WaitPullback";
            }
            else if (overheated && nearHigh)
            {
                targetDiscountPct = MaxDiscount(
                    currentEntryDiscountPct,
                    Math.Min(settings.AvoidEarlySpikeDiscountPct, settings.MaxDiscountPct));
                profile = "AvoidEarlySpike";
            }
            else if (h4Weakening)
            {
                targetDiscountPct = MaxDiscount(
                    currentEntryDiscountPct,
                    Math.Min(settings.ConfirmFirstDiscountPct, settings.MaxDiscountPct));
                profile = "ConfirmFirst";
            }
            else if (dailyStrong && constructiveH4)
            {
                targetDiscountPct = CapDiscount(
                    currentEntryDiscountPct,
                    Math.Min(
                        Math.Min(settings.ShallowContinuationMaxDiscountPct, settings.FastContinuationMaxDiscountPct),
                        settings.MaxDiscountPct));
                profile = "FastContinuationShallow";
            }

            return new SeriesEntryProfileDecision(
                targetDiscountPct,
                profile,
                dailyMidSlope,
                dailyRsiSlope,
                dailyMacdSlope,
                h4MidSlope,
                h4RsiSlope,
                h4MacdSlope);
        }

        private static decimal? ResolveH4TriangleEntryDiscountOverridePct(
            List<Candle> h4Candles,
            List<Candle>? entryCandles,
            H4TriangleEntrySettings settings,
            decimal? currentEntryDiscountPct)
        {
            if (!settings.Enabled || h4Candles.Count < settings.ImpulseAndDriftBars)
                return currentEntryDiscountPct;

            var window = h4Candles.TakeLast(settings.ImpulseAndDriftBars).ToList();
            var impulse = window[0];
            if (impulse.Close <= impulse.Open)
                return currentEntryDiscountPct;

            var impulseBodyPct = CalculateRelativeMovePct(impulse.Open, impulse.Close);
            if (impulseBodyPct < settings.MinImpulseBodyPct)
                return currentEntryDiscountPct;

            var impulseRange = impulse.High - impulse.Low;
            if (impulseRange <= 0m)
                return currentEntryDiscountPct;

            var driftCandles = window.Skip(1).ToList();
            var qualifyingDriftCandles = driftCandles
                .Where(c => CalculateRelativeMovePct(c.Open, c.Close) <= settings.MaxDriftBodyPct)
                .Where(c => (c.Low - impulse.Low) / impulseRange >= settings.MinSmallCandleLowPositionPctOfImpulse)
                .ToList();

            if (qualifyingDriftCandles.Count < (int)settings.MinDriftCandlesNearEdge)
                return currentEntryDiscountPct;

            var shortLowsMin = qualifyingDriftCandles.Min(x => x.Low);
            var targetEntryPrice = shortLowsMin * (1m - settings.EntryBufferBelowShortLowsPct);
            if (targetEntryPrice <= 0m)
                return currentEntryDiscountPct;

            var currentReferencePrice = (entryCandles != null && entryCandles.Count > 0
                    ? entryCandles[^1].Close
                    : h4Candles[^1].Close);

            if (currentReferencePrice <= 0m || targetEntryPrice >= currentReferencePrice)
                return currentEntryDiscountPct;

            var targetDiscountPct = (currentReferencePrice - targetEntryPrice) / currentReferencePrice;
            return targetDiscountPct > 0m
                ? targetDiscountPct
                : currentEntryDiscountPct;
        }

        private static bool IsBullishMinFirstSetup(
            string weeklyRegime,
            string weeklyDirection,
            string dailyRegime,
            string dailyDirection,
            string h4Regime,
            string h4Direction)
        {
            var weeklyBullish =
                weeklyDirection == nameof(BollingerFigureDirection.Up) &&
                (weeklyRegime is nameof(BollingerFigureRegime.Runaway) or
                                nameof(BollingerFigureRegime.Pullback) or
                                nameof(BollingerFigureRegime.Reacceleration));

            var dailyBullishCorrection =
                dailyDirection == nameof(BollingerFigureDirection.Up) &&
                (dailyRegime is nameof(BollingerFigureRegime.Runaway) or
                               nameof(BollingerFigureRegime.Pullback) or
                               nameof(BollingerFigureRegime.Collapse));

            var h4CorrectivePushDown =
                h4Direction == nameof(BollingerFigureDirection.Down) &&
                h4Regime == nameof(BollingerFigureRegime.Runaway);

            return weeklyBullish && dailyBullishCorrection && h4CorrectivePushDown;
        }

        private static decimal? MaxDiscount(decimal? currentValue, decimal candidateValue)
        {
            return currentValue == null || candidateValue > currentValue.Value
                ? candidateValue
                : currentValue;
        }

        private static decimal? CapDiscount(decimal? currentValue, decimal maxValue)
        {
            return currentValue == null || currentValue.Value > maxValue
                ? maxValue
                : currentValue;
        }

        private static decimal GetLatestValue(List<decimal> series)
        {
            return series.Count == 0
                ? 0m
                : series[^1];
        }

        private static decimal CalculateRelativeMovePct(decimal from, decimal to)
        {
            return from <= 0m
                ? 0m
                : decimal.Abs(to - from) / from;
        }

        private bool ShouldBypassWishListFilterForLiveScan(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            decimal entryScore)
        {
            var needsDeeperEntry = ResolveNeedsDeeperEntry(snapshot, diagnostics);
            var needsMomentumExit = ResolveNeedsMomentumExit(snapshot, diagnostics, entryScore);

            return IsParabolicExpansionProxy(snapshot, diagnostics, needsDeeperEntry, needsMomentumExit) ||
                   IsDeepParabolicExpansionProxy(snapshot, diagnostics, needsDeeperEntry) ||
                   IsExplosiveMinFirstProxy(snapshot, diagnostics, needsDeeperEntry, needsMomentumExit) ||
                   IsExplosiveMaxFirstProxy(snapshot, diagnostics) ||
                   IsExplosiveBreakoutProxy(snapshot);
        }

        private static bool ShouldBypassWeeklyBbVetoForLiveMover(
            StockInfo stock,
            string presetScanCode,
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            decimal entryScore)
        {
            if (!IsLiveMoverPreset(presetScanCode))
                return false;

            var rank = stock.Rank > 0 ? stock.Rank : int.MaxValue;
            if (snapshot.Current.DistanceTo20dHigh > -0.25m)
                return false;

            var aboveAllMeanContinuation =
                rank <= 50 &&
                (snapshot.Current.WeeklyMaSignedDistancePct ?? 0m) > 0m &&
                snapshot.Current.DailyMaSignedDistancePct > 0m &&
                snapshot.Current.H4MaSignedDistancePct > 0m &&
                (snapshot.DailyMaDelta3 > 0m ||
                 snapshot.H4MaDelta3 > 0m ||
                 entryScore >= 20m) &&
                (diagnostics.ATRRatio >= 2.5m ||
                 snapshot.Current.DailyRSI14 >= 50m ||
                 entryScore >= 20m);

            var strongTopRankMover =
                rank <= 10 &&
                (diagnostics.ATRRatio >= 4m ||
                 snapshot.Current.DailyRSI14 >= 60m ||
                 entryScore >= 20m);

            var constructiveLiveRecovery =
                rank <= 50 &&
                snapshot.Current.DailyMaSignedDistancePct > 0m &&
                snapshot.DailyMaDelta3 > 0m &&
                snapshot.H4MaDelta3 > 0m &&
                snapshot.DailyRsiDelta3 > -2m &&
                (diagnostics.ATRRatio >= 3.5m ||
                 snapshot.Current.DailyRSI14 >= 55m ||
                 entryScore >= 20m);

            return aboveAllMeanContinuation || strongTopRankMover || constructiveLiveRecovery;
        }

        private static bool IsLiveMoverPreset(string presetScanCode)
        {
            return string.Equals(presetScanCode, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(presetScanCode, "TOP_OPEN_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(presetScanCode, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(presetScanCode, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGainPreset(string presetScanCode) =>
            string.Equals(presetScanCode, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(presetScanCode, "TOP_OPEN_PERC_GAIN", StringComparison.OrdinalIgnoreCase);

        private static bool IsLossPreset(string presetScanCode) =>
            string.Equals(presetScanCode, "TOP_PERC_LOSE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(presetScanCode, "TOP_OPEN_PERC_LOSE", StringComparison.OrdinalIgnoreCase);

        private static bool IsNonDirectionalLivePreset(string presetScanCode) =>
            string.Equals(presetScanCode, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(presetScanCode, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase);

        private static decimal ResolvePresetBonus(string presetScanCode, NextDayRankingSettings settings)
        {
            return presetScanCode switch
            {
                "HOT_BY_VOLUME" => settings.HotByVolumePresetBonus,
                "MOST_ACTIVE" => settings.MostActivePresetBonus,
                "TOP_PERC_GAIN" => settings.TopPercGainPresetBonus,
                "TOP_PERC_LOSE" => settings.TopPercLosePresetBonus,
                "TOP_OPEN_PERC_GAIN" => settings.TopOpenPercGainPresetBonus,
                "TOP_OPEN_PERC_LOSE" => settings.TopOpenPercLosePresetBonus,
                _ => settings.DefaultPresetBonus
            };
        }

        private static decimal Clamp01(decimal value)
        {
            if (value <= 0m)
                return 0m;

            if (value >= 1m)
                return 1m;

            return value;
        }

        private bool ResolveNeedsDeeperEntry(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics)
        {
            var settings = _getCandidatesSettingsProvider.Get().TradePlan.DeepPullbackEntry;
            if (!settings.Enabled)
                return false;

            var signals = 0;

            if (snapshot.Current.DistanceTo20dHigh <= settings.DistanceTo20dHighThreshold)
                signals++;

            if (diagnostics.DailyTrendPosition <= settings.DailyTrendPositionThreshold)
                signals++;

            if (diagnostics.TrendPosition <= settings.TrendPositionThreshold)
                signals++;

            if (snapshot.Current.DailyRSI14 <= settings.DailyRsi14Threshold)
                signals++;

            return signals >= settings.MinSignalsRequired;
        }

        private bool ResolveNeedsMomentumExit(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            decimal entryScore)
        {
            var settings = _getCandidatesSettingsProvider.Get().TradePlan.MomentumExit;
            if (!settings.Enabled)
                return false;

            var signals = 0;

            if (entryScore >= settings.EntryScoreThreshold)
                signals++;

            if (diagnostics.TrendPosition >= settings.TrendPositionThreshold)
                signals++;

            if (diagnostics.DailyTrendPosition >= settings.DailyTrendPositionThreshold)
                signals++;

            if (diagnostics.ATRRatio >= settings.AtrRatioThreshold)
                signals++;

            return signals >= settings.MinSignalsRequired;
        }

        private decimal? ResolveDeepPullbackEntryDiscountPct(
            CandidateSignalSnapshot snapshot,
            List<Candle> candles,
            CandidateDiagnostics? diagnostics = null,
            bool isConstructiveDeepMinFirst = false)
        {
            diagnostics ??= BuildDiagnostics(snapshot, candles);
            if (!ResolveNeedsDeeperEntry(snapshot, diagnostics))
                return null;

            if (isConstructiveDeepMinFirst)
            {
                var constructiveSettings = _getCandidatesSettingsProvider.Get().TradePlan.ConstructiveDeepMinFirst;
                return diagnostics.ATRRatio >= constructiveSettings.HighAtrRatioThreshold
                    ? constructiveSettings.HighAtrEntryDiscountPct
                    : constructiveSettings.EntryDiscountPct;
            }

            var settings = _getCandidatesSettingsProvider.Get().TradePlan.DeepPullbackEntry;

            return diagnostics.ATRRatio >= settings.HighAtrRatioThreshold
                ? settings.HighAtrEntryDiscountPct
                : settings.EntryDiscountPct;
        }

        private bool IsStrongMinFirstProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            bool needsDeeperEntry,
            bool needsMomentumExit)
        {
            var settings = _getCandidatesSettingsProvider.Get().TradePlan.StrongMinFirstExit;
            if (!settings.Enabled || needsDeeperEntry || needsMomentumExit)
                return false;

            return diagnostics.DailyTrendPosition >= settings.DailyTrendPositionThreshold &&
                   diagnostics.TrendPosition >= settings.TrendPositionThreshold &&
                   diagnostics.ATRRatio <= settings.MaxAtrRatio &&
                   snapshot.Current.DistanceTo20dHigh <= settings.MaxDistanceTo20dHigh;
        }

        private bool IsWeakDeepPullbackProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            bool needsDeeperEntry)
        {
            var settings = _getCandidatesSettingsProvider.Get().TradePlan.WeakDeepPullbackExit;
            if (!settings.Enabled || !needsDeeperEntry)
                return false;

            var constructiveSettings = _getCandidatesSettingsProvider.Get().TradePlan.ConstructiveDeepMinFirst;
            var isConstructive =
                constructiveSettings.Enabled &&
                diagnostics.DailyTrendPosition >= constructiveSettings.MinDailyTrendPosition &&
                diagnostics.TrendPosition >= constructiveSettings.MinTrendPosition &&
                diagnostics.ATRRatio >= constructiveSettings.MinAtrRatio &&
                snapshot.Current.DailyRSI14 >= constructiveSettings.MinDailyRsi14;

            if (isConstructive)
                return false;

            return diagnostics.DailyTrendPosition <= settings.MaxDailyTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio;
        }

        private bool IsExplosiveMinFirstProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            bool needsDeeperEntry,
            bool needsMomentumExit)
        {
            var settings = _getCandidatesSettingsProvider.Get().TradePlan.ExplosiveMinFirstExit;
            if (!settings.Enabled || needsDeeperEntry || !needsMomentumExit)
                return false;

            if (IsParabolicExpansionProxy(snapshot, diagnostics, needsDeeperEntry, needsMomentumExit))
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   snapshot.Current.DailyRSI14 >= settings.MinDailyRsi14 &&
                   snapshot.Current.DistanceTo20dHigh <= settings.MaxDistanceTo20dHigh;
        }

        private bool IsConstructiveDeepMinFirstProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            bool needsDeeperEntry)
        {
            var settings = _getCandidatesSettingsProvider.Get().TradePlan.ConstructiveDeepMinFirst;
            if (!settings.Enabled || !needsDeeperEntry)
                return false;

            if (IsDeepParabolicExpansionProxy(snapshot, diagnostics, needsDeeperEntry))
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   snapshot.Current.DailyRSI14 >= settings.MinDailyRsi14;
        }

        private bool IsParabolicExpansionProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            bool needsDeeperEntry,
            bool needsMomentumExit)
        {
            var settings = _getCandidatesSettingsProvider.Get().TradePlan.ParabolicExpansionExit;
            if (!settings.Enabled || needsDeeperEntry || !needsMomentumExit)
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   snapshot.Current.DailyRSI14 >= settings.MinDailyRsi14 &&
                   snapshot.Current.DistanceTo20dHigh >= settings.MaxDistanceTo20dHigh &&
                   diagnostics.VolumeRatio20 >= settings.MinVolumeRatio20;
        }

        private bool IsExplosiveMaxFirstProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics)
        {
            var settings = _getCandidatesSettingsProvider.Get().TradePlan.ExplosiveMaxFirstExit;
            if (!settings.Enabled)
                return false;

            return snapshot.Current.DailyRSI14 >= settings.MinDailyRsi14 &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   diagnostics.VolumeRatio20 <= settings.MaxVolumeRatio20;
        }

        private static bool IsDeepLaunchProxy(CandidateSignalSnapshot snapshot)
        {
            var f = snapshot.Current;

            return f.DistanceTo20dHigh <= -12m &&
                   f.DailyBollingerBandWidthPct >= 45m &&
                   (f.WeeklyBollingerBandWidthPct ?? 0m) >= 70m &&
                   snapshot.DailyMaDelta3 > 0m &&
                   snapshot.DailyRsiDelta3 > 0m;
        }

        private static bool IsExplosiveBreakoutProxy(CandidateSignalSnapshot snapshot)
        {
            var f = snapshot.Current;

            return f.DistanceTo20dHigh <= -6m &&
                   f.DailyBollingerBandWidthPct >= 70m &&
                   f.DailyRSI14 >= 60m &&
                   f.DailyMACDLineMinusSignal > 0m &&
                   snapshot.DailyMaDelta3 > 0m &&
                   (!f.WeeklyMACDLineMinusSignal.HasValue || f.WeeklyMACDLineMinusSignal.Value <= 5m);
        }

        private static bool IsEarlyReversalProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings)
        {
            var f = snapshot.Current;

            return f.DistanceTo20dHigh <= settings.EarlyReversalMaxDistanceTo20dHigh &&
                   f.DailyRSI14 >= settings.EarlyReversalMinDailyRsi14 &&
                   f.DailyRSI14 <= settings.EarlyReversalMaxDailyRsi14 &&
                   diagnostics.ATRRatio >= settings.EarlyReversalMinAtrRatio &&
                   diagnostics.VolumeRatio20 <= settings.EarlyReversalMaxVolumeRatio20 &&
                   diagnostics.TrendPosition <= settings.EarlyReversalMaxTrendPosition &&
                   diagnostics.DailyTrendPosition <= settings.EarlyReversalMaxDailyTrendPosition &&
                   snapshot.DailyMaDelta3 > 0m &&
                   snapshot.DailyRsiDelta3 > 0m;
        }

        private static bool IsReversalDeepHookProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings)
        {
            var f = snapshot.Current;

            return f.DistanceTo20dHigh <= settings.ReversalDeepHookMaxDistanceTo20dHigh &&
                   f.DailyRSI14 >= settings.ReversalDeepHookMinDailyRsi14 &&
                   f.DailyRSI14 <= settings.ReversalDeepHookMaxDailyRsi14 &&
                   diagnostics.ATRRatio >= settings.ReversalDeepHookMinAtrRatio &&
                   diagnostics.TrendPosition <= settings.ReversalDeepHookMaxTrendPosition &&
                   diagnostics.DailyTrendPosition <= settings.ReversalDeepHookMaxDailyTrendPosition &&
                   diagnostics.BBMidSignedDistancePct <= settings.ReversalDeepHookMaxBbMid &&
                   !IsAnomalousVolatilityProxy(diagnostics, settings);
        }

        private static bool IsReversalH4BellUpHybridProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings,
            RecentFeatureSeries recentSeries,
            BollingerStateSet bbState)
        {
            var bellPatternSignal = ClassifyBellPatternSignal(bbState, recentSeries);
            if (bellPatternSignal.Kind != BellPatternKind.BellUp ||
                bellPatternSignal.Timeframe != BellPatternTimeframe.H4)
            {
                return false;
            }

            var f = snapshot.Current;

            return f.DistanceTo20dHigh <= settings.ReversalH4BellUpMaxDistanceTo20dHigh &&
                   f.DailyRSI14 >= settings.ReversalH4BellUpMinDailyRsi14 &&
                   f.DailyRSI14 <= settings.ReversalH4BellUpMaxDailyRsi14 &&
                   diagnostics.ATRRatio >= settings.ReversalH4BellUpMinAtrRatio &&
                   !IsAnomalousVolatilityProxy(diagnostics, settings) &&
                   !IsExhaustedMoverProxy(snapshot, diagnostics, settings);
        }

        private static bool IsReversalHighAmplitudeProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings)
        {
            var f = snapshot.Current;

            var deepEnough =
                f.DistanceTo20dHigh <= settings.ReversalHighAmplitudeMaxDistanceTo20dHigh &&
                (diagnostics.Pullback10d <= settings.ReversalHighAmplitudeMaxPullback10d ||
                 diagnostics.DailyPullback10d <= settings.ReversalHighAmplitudeMaxDailyPullback10d);
            var earlyRsiHook =
                f.DailyRSI14 >= settings.ReversalHighAmplitudeMinDailyRsi14 &&
                f.DailyRSI14 <= settings.ReversalHighAmplitudeEarlyRsiMax;
            var midRsiHook =
                f.DailyRSI14 > settings.ReversalHighAmplitudeEarlyRsiMax &&
                f.DailyRSI14 <= settings.ReversalHighAmplitudeMaxDailyRsi14 &&
                diagnostics.DailyTrendPosition >= settings.ReversalHighAmplitudeMidRsiMinDailyTrendPosition &&
                diagnostics.BBMidSignedDistancePct >= settings.ReversalHighAmplitudeMidRsiMinBbMid;

            return deepEnough &&
                   (earlyRsiHook || midRsiHook) &&
                   diagnostics.ATRRatio >= settings.ReversalHighAmplitudeMinAtrRatio &&
                   diagnostics.TrendPosition >= settings.ReversalHighAmplitudeMinTrendPosition &&
                   diagnostics.DailyTrendPosition >= settings.ReversalHighAmplitudeMinDailyTrendPosition &&
                   diagnostics.BBMidSignedDistancePct >= settings.ReversalHighAmplitudeMinBbMid &&
                   !IsAnomalousVolatilityProxy(diagnostics, settings) &&
                   !IsExhaustedMoverProxy(snapshot, diagnostics, settings);
        }

        private static bool IsReversalMatureWeakBounceProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings)
        {
            var f = snapshot.Current;

            return f.DailyRSI14 >= settings.ReversalMatureWeakBounceMinDailyRsi14 &&
                   f.DistanceTo20dHigh >= settings.ReversalMatureWeakBounceMinDistanceTo20dHigh &&
                   diagnostics.BBMidSignedDistancePct <= settings.ReversalMatureWeakBounceMaxBbMid &&
                   diagnostics.DailyTrendPosition < settings.DailyTrendNegativePenaltyThreshold &&
                   !IsAnomalousVolatilityProxy(diagnostics, settings);
        }

        private static bool IsReversalSeriesRecoveryProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            RecentFeatureSeries recentSeries,
            NextDayRankingSettings settings)
        {
            if (snapshot.Current.DistanceTo20dHigh > settings.ReversalSeriesMaxDistanceTo20dHigh ||
                snapshot.Current.DailyRSI14 > settings.ReversalSeriesMaxDailyRsi14 ||
                diagnostics.ATRRatio < settings.ReversalSeriesMinAtrRatio ||
                IsAnomalousVolatilityProxy(diagnostics, settings) ||
                IsExhaustedMoverProxy(snapshot, diagnostics, settings))
            {
                return false;
            }

            var dailyMidSlope = CalculateRelativeSlopePct(recentSeries.DailyBbMidBandSeries);
            var dailyWidthTail = CalculateTailBandWidthDeltaPct(
                recentSeries.DailyBbUpperBandSeries,
                recentSeries.DailyBbMidBandSeries,
                recentSeries.DailyBbLowerBandSeries,
                4);
            var h4WidthTail = CalculateTailBandWidthDeltaPct(
                recentSeries.H4BbUpperBandSeries,
                recentSeries.H4BbMidBandSeries,
                recentSeries.H4BbLowerBandSeries,
                4);
            var h4MacdTail = CalculateTailSlope(recentSeries.H4MacdHistogramSeries, 4);
            var h4RsiTail = CalculateTailSlope(recentSeries.H4RsiSeries, 4);
            var h4MacdLeg =
                h4MacdTail >= settings.ReversalSeriesMinH4MacdTailSlope &&
                h4RsiTail >= settings.ReversalSeriesMinH4RsiTailForMacdLeg;

            return dailyMidSlope <= settings.ReversalSeriesMaxDailyMidSlopePct &&
                   dailyWidthTail <= -settings.ReversalSeriesMinDailyWidthCompressionPct &&
                   (h4MacdLeg ||
                    h4WidthTail >= settings.ReversalSeriesMinH4WidthExpansionPct ||
                    h4RsiTail >= settings.ReversalSeriesMinH4RsiTailSlope);
        }

        private static bool IsReversalWeakContinuationProxy(
            CandidateSignalSnapshot snapshot,
            RecentFeatureSeries recentSeries,
            NextDayRankingSettings settings)
        {
            if (snapshot.Current.DailyRSI14 > settings.ReversalWeakMaxDailyRsi14)
                return false;

            var h4MidSlope = CalculateRelativeSlopePct(recentSeries.H4BbMidBandSeries);
            var h4RsiTail = CalculateTailSlope(recentSeries.H4RsiSeries, 4);
            var h4MacdTail = CalculateTailSlope(recentSeries.H4MacdHistogramSeries, 4);

            return h4MidSlope <= settings.ReversalWeakMaxH4MidSlopePct &&
                   h4RsiTail <= settings.ReversalWeakMaxH4RsiTailSlope &&
                   h4MacdTail <= settings.ReversalWeakMaxH4MacdTailSlope;
        }

        private static bool IsReversalConstructiveDeepBounceProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings,
            decimal candidateScore,
            decimal entryScore)
        {
            var f = snapshot.Current;
            var deepEnough =
                diagnostics.Pullback10d <= settings.ReversalConstructiveDeepBounceMaxPullback10d ||
                diagnostics.DailyPullback10d <= settings.ReversalConstructiveDeepBounceMaxDailyPullback10d;
            var qualityEnough =
                candidateScore >= settings.ReversalConstructiveDeepBounceMinCandidateScore ||
                (entryScore >= settings.ReversalConstructiveDeepBounceStrongEntryScore &&
                 diagnostics.BBMidSignedDistancePct >= settings.ReversalConstructiveDeepBounceStrongEntryMinBbMid);

            return entryScore >= settings.ReversalConstructiveDeepBounceMinEntryScore &&
                   qualityEnough &&
                   f.DailyRSI14 >= settings.ReversalConstructiveDeepBounceMinDailyRsi14 &&
                   f.DailyRSI14 <= settings.ReversalConstructiveDeepBounceMaxDailyRsi14 &&
                   diagnostics.ATRRatio >= settings.ReversalConstructiveDeepBounceMinAtrRatio &&
                   f.DistanceTo20dHigh <= settings.ReversalConstructiveDeepBounceMaxDistanceTo20dHigh &&
                   deepEnough &&
                   diagnostics.DailyTrendPosition >= settings.ReversalConstructiveDeepBounceMinDailyTrendPosition &&
                   diagnostics.BBMidSignedDistancePct >= settings.ReversalConstructiveDeepBounceMinBbMid &&
                   !IsAnomalousVolatilityProxy(diagnostics, settings) &&
                   !IsExhaustedMoverProxy(snapshot, diagnostics, settings);
        }

        private static bool IsReversalBrokenDownProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings)
        {
            var f = snapshot.Current;

            return f.DistanceTo20dHigh <= settings.ReversalBrokenDownMaxDistanceTo20dHigh &&
                   diagnostics.BBMidSignedDistancePct <= settings.ReversalBrokenDownMaxBbMid &&
                   diagnostics.TrendPosition <= settings.ReversalBrokenDownMaxTrendPosition &&
                   diagnostics.DailyTrendPosition <= settings.ReversalBrokenDownMaxDailyTrendPosition &&
                   !IsAnomalousVolatilityProxy(diagnostics, settings);
        }

        private bool IsDeepParabolicExpansionProxy(
            CandidateSignalSnapshot snapshot,
            CandidateDiagnostics diagnostics,
            bool needsDeeperEntry)
        {
            var settings = _getCandidatesSettingsProvider.Get().TradePlan.DeepParabolicExpansionExit;
            if (!settings.Enabled || !needsDeeperEntry)
                return false;

            return diagnostics.DailyTrendPosition >= settings.MinDailyTrendPosition &&
                   diagnostics.TrendPosition >= settings.MinTrendPosition &&
                   diagnostics.ATRRatio >= settings.MinAtrRatio &&
                   snapshot.Current.DailyRSI14 >= settings.MinDailyRsi14 &&
                   diagnostics.VolumeRatio20 >= settings.MinVolumeRatio20;
        }

        private static CandidateDiagnostics BuildDiagnostics(
            CandidateSignalSnapshot snapshot,
            List<Candle> candles)
        {
            return new CandidateDiagnostics
            {
                Pullback10d = CalculatePullbackByCalendarDays(candles, 10),
                VolumeRatio20 = CalculateVolumeRatio20(candles),
                ATRRatio = CalculateAtrRatio(candles, 14),
                TrendPosition = snapshot.Current.H4MaSignedDistancePct,
                DailyTrendPosition = snapshot.Current.DailyMaSignedDistancePct,
                DailyPullback10d = CalculateDailyPullback10d(candles),
                BBMidSignedDistancePct = CalculateSmaSignedDistancePct(candles, 20),
                WeeklyMACDHistDelta = CalculateWeeklyMacdHistDelta(candles)
            };
        }

        private (int? ExpectedBarsToTarget, DateTime? ExpectedTargetMarketTime) CalculateWishListTargetForecast(
            string ticker,
            CandidateSignalSnapshot snapshot,
            RecentFeatureSeries recentSeries,
            List<Candle> candles,
            DateTime scanTimeMarket)
        {
            var currentDistancePct = snapshot.Current.DailyMaSignedDistancePct;

            _logger.Info(
                $"Scan target forecast input: {ticker}. " +
                $"CurrentDailyDistancePct={currentDistancePct}, " +
                $"DailyMaDelta3={snapshot.DailyMaDelta3}, " +
                $"H4MaDelta3={snapshot.H4MaDelta3}, " +
                $"DailyRsiDelta3={snapshot.DailyRsiDelta3}, " +
                $"DailyMacdDelta3={snapshot.DailyMacdDelta3}");

            if (currentDistancePct >= 0m)
            {
                _logger.Info($"Scan target forecast: {ticker} reached/exceeded daily mid already.");
                return (0, scanTimeMarket);
            }

            decimal progressPerBar = 0m;
            string progressSource = "none";

            if (snapshot.DailyMaDelta3 > 0m)
            {
                progressPerBar = snapshot.DailyMaDelta3 / 3m;
                progressSource = "daily-ma";
            }
            else if (snapshot.H4MaDelta3 > 0m)
            {
                progressPerBar = snapshot.H4MaDelta3 / 3m;
                progressSource = "h4-ma";
            }
            else if (snapshot.DailyRsiDelta3 > 0m || snapshot.DailyMacdDelta3 > 0m)
            {
                progressPerBar = Math.Abs(currentDistancePct) / 12m;
                progressSource = "daily-rsi-macd-fallback";
            }
            else if (IsReversalBaseRecoveryCandidate(snapshot, recentSeries, candles))
            {
                progressPerBar = Math.Abs(currentDistancePct) / 10m;
                progressSource = "h4-reversal-base";
            }

            _logger.Info(
                $"Scan target forecast progress: {ticker}. " +
                $"ProgressSource={progressSource}, " +
                $"ProgressPerBar={progressPerBar}");

            if (progressPerBar <= 0m)
            {
                _logger.Info($"Scan target forecast failed: {ticker}. No positive progress signal.");
                return (null, null);
            }

            var remainingDistancePct = Math.Abs(currentDistancePct);
            var expectedBars = (int)Math.Ceiling((double)(remainingDistancePct / progressPerBar));

            _logger.Info(
                $"Scan target forecast raw result: {ticker}. " +
                $"RemainingDistancePct={remainingDistancePct}, " +
                $"ExpectedBarsRaw={expectedBars}");

            if (expectedBars <= 0)
            {
                _logger.Info($"Scan target forecast normalized to zero bars: {ticker}.");
                return (0, scanTimeMarket);
            }

            var cappedBars = Math.Min(expectedBars, 60);
            var step = EstimateMarketBarStep(candles);
            var expectedTargetMarketTime = scanTimeMarket.Add(step * cappedBars);

            _logger.Info(
                $"Scan target forecast final result: {ticker}. " +
                $"ExpectedBarsCapped={cappedBars}, " +
                $"Step={step}, " +
                $"ExpectedTargetMarketTime={expectedTargetMarketTime:yyyy-MM-dd HH:mm:ss}");

            return (cappedBars, expectedTargetMarketTime);
        }

        private bool IsReversalBaseRecoveryCandidate(
            CandidateSignalSnapshot snapshot,
            RecentFeatureSeries recentSeries,
            List<Candle> candles)
        {
            if (snapshot.Current.DailyMaSignedDistancePct >= 0m)
                return false;

            if ((snapshot.Current.WeeklyMaSignedDistancePct ?? decimal.MinValue) <= 0m)
                return false;

            if (recentSeries.H4MacdHistogramSeries.Count < 4 || candles.Count < 5)
                return false;

            var bbState = BuildBollingerStateSet(recentSeries);
            var bellPatternKind = ClassifyBellPatternKind(bbState, recentSeries);
            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdHistogramSeries);
            var h4MacdImproving =
                h4MacdSlope > 0m &&
                recentSeries.H4MacdHistogramSeries[^1] > recentSeries.H4MacdHistogramSeries[^2] &&
                recentSeries.H4MacdHistogramSeries[^2] >= recentSeries.H4MacdHistogramSeries[^3];

            if (bellPatternKind == BellPatternKind.BellDown && h4MacdImproving)
                return true;

            if (!h4MacdImproving)
                return false;

            var recentH4Candles = candles.TakeLast(5).ToList();
            var higherLowCount = 0;

            for (var i = 1; i < recentH4Candles.Count; i++)
            {
                if (recentH4Candles[i].Low >= recentH4Candles[i - 1].Low)
                    higherLowCount++;
            }

            var positiveCloses = 0;
            for (var i = Math.Max(0, recentH4Candles.Count - 3); i < recentH4Candles.Count; i++)
            {
                if (recentH4Candles[i].Close >= recentH4Candles[i].Open)
                    positiveCloses++;
            }

            return higherLowCount >= 2 && positiveCloses >= 1;
        }

        private static TimeSpan EstimateMarketBarStep(List<Candle> candles)
        {
            if (candles.Count < 2)
                return TimeSpan.FromHours(4);

            var steps = new List<TimeSpan>();

            for (var i = Math.Max(1, candles.Count - 10); i < candles.Count; i++)
            {
                var step = candles[i].Time - candles[i - 1].Time;

                if (step > TimeSpan.Zero)
                    steps.Add(step);
            }

            if (steps.Count == 0)
                return TimeSpan.FromHours(4);

            var ordered = steps.OrderBy(x => x).ToList();
            return ordered[ordered.Count / 2];
        }

        private static decimal CalculatePullbackByCalendarDays(List<Candle> candles, int days)
        {
            if (candles.Count == 0)
                return 0m;

            var lastTime = candles[^1].Time;
            var start = lastTime.AddDays(-days);

            var range = candles
                .Where(x => x.Time >= start)
                .ToList();

            if (range.Count == 0)
                return 0m;

            var highest = range.Max(x => x.High);
            var close = candles[^1].Close;

            if (highest <= 0m)
                return 0m;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalculateVolumeRatio20(List<Candle> candles)
        {
            if (candles.Count < 21)
                return 0m;

            var currentVolume = candles[^1].Volume;

            var avgVolume20 = candles
                .Skip(candles.Count - 21)
                .Take(20)
                .Average(x => x.Volume);

            if (avgVolume20 <= 0m)
                return 0m;

            return currentVolume / avgVolume20;
        }

        private static decimal CalculateAtrRatio(List<Candle> candles, int length)
        {
            if (candles.Count < length + 1)
                return 0m;

            var trueRanges = new List<decimal>();

            for (var i = candles.Count - length; i < candles.Count; i++)
            {
                var current = candles[i];
                var prevClose = candles[i - 1].Close;

                var tr = Math.Max(
                    current.High - current.Low,
                    Math.Max(
                        Math.Abs(current.High - prevClose),
                        Math.Abs(current.Low - prevClose)));

                trueRanges.Add(tr);
            }

            if (trueRanges.Count == 0)
                return 0m;

            var atr = trueRanges.Average();
            var close = candles[^1].Close;

            if (close == 0m)
                return 0m;

            return atr / close * 100m;
        }

        private static decimal CalculateDailyPullback10d(List<Candle> candles)
        {
            var dailyBars = BuildDailyBars(candles);

            if (dailyBars.Count == 0)
                return 0m;

            var range = dailyBars.TakeLast(10).ToList();
            var highest = range.Max(x => x.High);
            var close = dailyBars[^1].Close;

            if (highest <= 0m)
                return 0m;

            return (close - highest) / highest * 100m;
        }

        private static decimal CalculateSmaSignedDistancePct(List<Candle> candles, int length)
        {
            if (candles.Count < length)
                return 0m;

            var sma = candles.TakeLast(length).Average(x => x.Close);

            if (sma == 0m)
                return 0m;

            var close = candles[^1].Close;
            return (close - sma) / sma * 100m;
        }

        private static decimal? CalculateWeeklyMacdHistDelta(List<Candle> candles)
        {
            var weeklyCloses = BuildWeeklyBars(candles)
                .Select(x => x.Close)
                .ToList();

            var macdSeries = BuildMacdSeries(weeklyCloses);

            if (macdSeries.Count < 2)
                return null;

            var currentHist = macdSeries[^1].Macd - macdSeries[^1].Signal;
            var prevHist = macdSeries[^2].Macd - macdSeries[^2].Signal;

            return currentHist - prevHist;
        }

        private static List<Candle> BuildDailyBars(List<Candle> candles)
        {
            var result = new List<Candle>();

            foreach (var group in candles.GroupBy(x => x.Time.Date).OrderBy(x => x.Key))
            {
                var ordered = group.OrderBy(x => x.Time).ToList();

                // Close from the last regular-session bar, not the last bar of the calendar day
                // (which would be an after-hours print when extended hours are cached).
                var sessionBars = ordered.Where(x => x.Time.TimeOfDay < RegularSessionEnd).ToList();
                if (sessionBars.Count == 0)
                    sessionBars = ordered;

                result.Add(new Candle
                {
                    Timeframe = Timeframe.D1,
                    Time = group.Key,
                    Open = ordered[0].Open,
                    High = ordered.Max(x => x.High),
                    Low = ordered.Min(x => x.Low),
                    Close = sessionBars[^1].Close,
                    Volume = ordered.Sum(x => x.Volume)
                });
            }

            return result;
        }

        private bool TryRejectByRecentDailyPriceFloor(
            string ticker,
            List<Candle> dailyBars,
            out string reason)
        {
            var settings = _getCandidatesSettingsProvider.Get().PreFilter;
            reason = string.Empty;

            if (settings.RecentDailyPriceFloorDays <= 0 ||
                (settings.MinRecentDailyClosePrice <= 0m && settings.MinRecentDailyLowPrice <= 0m) ||
                dailyBars.Count == 0)
            {
                return false;
            }

            var completedDailyBars = dailyBars.Count > 1
                ? dailyBars.Take(dailyBars.Count - 1).ToList()
                : dailyBars;

            if (completedDailyBars.Count == 0)
                completedDailyBars = dailyBars;

            var recentWindow = Math.Max(settings.RecentDailyPriceFloorDays, RecentDailySeriesLength);
            var recentBars = completedDailyBars
                .TakeLast(recentWindow)
                .ToList();

            if (recentBars.Count == 0)
                return false;

            var minRecentClose = recentBars.Min(x => x.Close);
            var minRecentLow = recentBars.Min(x => x.Low);
            var maxRecentClose = recentBars.Max(x => x.Close);
            var recoveredWellAboveCloseFloor =
                settings.MinRecentDailyClosePrice > 0m &&
                maxRecentClose >= settings.MinRecentDailyClosePrice + settings.RecoveredCloseFloorBuffer;
            var closeFloorTolerance = Math.Max(0m, settings.RecentDailyCloseFloorTolerance);

            if (settings.MinRecentDailyClosePrice > 0m &&
                minRecentClose < settings.MinRecentDailyClosePrice &&
                !(minRecentClose >= settings.MinRecentDailyClosePrice - closeFloorTolerance && recoveredWellAboveCloseFloor))
            {
                reason =
                    $"recent daily close floor veto. " +
                    $"MinCloseLast{recentBars.Count}D={_fmt.Price(minRecentClose)}, " +
                    $"Required>={_fmt.Price(settings.MinRecentDailyClosePrice)}";
                return true;
            }

            if (settings.MinRecentDailyLowPrice > 0m &&
                minRecentLow < settings.MinRecentDailyLowPrice)
            {
                reason =
                    $"recent daily low floor veto. " +
                    $"MinLowLast{recentBars.Count}D={_fmt.Price(minRecentLow)}, " +
                    $"Required>={_fmt.Price(settings.MinRecentDailyLowPrice)}";
                return true;
            }

            return false;
        }

        private static List<Candle> BuildWeeklyBars(List<Candle> candles)
        {
            var result = new List<Candle>();

            foreach (var group in candles
                .GroupBy(x => StartOfWeek(x.Time.Date))
                .OrderBy(x => x.Key))
            {
                var ordered = group.OrderBy(x => x.Time).ToList();

                result.Add(new Candle
                {
                    Timeframe = Timeframe.W1,
                    Time = group.Key,
                    Open = ordered[0].Open,
                    High = ordered.Max(x => x.High),
                    Low = ordered.Min(x => x.Low),
                    Close = ordered[^1].Close,
                    Volume = ordered.Sum(x => x.Volume)
                });
            }

            return result;
        }

        private static DateTime StartOfWeek(DateTime value)
        {
            var diff = (7 + (value.DayOfWeek - DayOfWeek.Monday)) % 7;
            return value.AddDays(-diff).Date;
        }

        private static DateTime StartOfH4Bucket(DateTime value)
        {
            return new DateTime(
                value.Year,
                value.Month,
                value.Day,
                (value.Hour / 4) * 4,
                0,
                0,
                value.Kind);
        }

        private static List<MacdPoint> BuildMacdSeries(List<decimal> closes)
        {
            var result = new List<MacdPoint>();

            if (closes.Count == 0)
                return result;

            decimal? ema12 = null;
            decimal? ema26 = null;
            decimal? signal = null;

            const decimal k12 = 2m / 13m;
            const decimal k26 = 2m / 27m;
            const decimal k9 = 2m / 10m;

            foreach (var close in closes)
            {
                ema12 = ema12 == null ? close : ema12.Value + (close - ema12.Value) * k12;
                ema26 = ema26 == null ? close : ema26.Value + (close - ema26.Value) * k26;

                var macd = ema12.Value - ema26.Value;
                signal = signal == null ? macd : signal.Value + (macd - signal.Value) * k9;

                result.Add(new MacdPoint
                {
                    Macd = macd,
                    Signal = signal.Value
                });
            }

            return result;
        }

        private static decimal CalculateAverageDollarVolumeDaily(List<Candle> candles, int period)
        {
            var actualPeriod = Math.Max(1, period);

            var dailyDollarVolumes = candles
                .GroupBy(x => x.Time.Date)
                .Select(g =>
                {
                    var ordered = g.OrderBy(x => x.Time).ToList();
                    var dayClose = ordered[^1].Close;
                    var dayVolume = ordered.Sum(x => x.Volume);
                    return dayClose * dayVolume;
                })
                .TakeLast(actualPeriod)
                .ToList();

            if (dailyDollarVolumes.Count == 0)
                return 0m;

            return dailyDollarVolumes.Average();
        }

        private static decimal CalculatePercent(decimal from, decimal to)
        {
            if (from == 0m)
                return 0m;

            return (to - from) / from * 100m;
        }

        private static string BuildWishListNotes(CandidateSignalSnapshot snapshot)
        {
            var parts = new List<string>();

            if (snapshot.Current.DailyMACDLineMinusSignal <= 0m)
                parts.Add("daily MACD weak/negative");

            if (snapshot.Current.DailyRSI14 < 50m)
                parts.Add("daily RSI below neutral");

            if (snapshot.DailyMaDelta3 > 0m || snapshot.DailyRsiDelta3 > 0m || snapshot.DailyMacdDelta3 > 0m)
                parts.Add("early daily improvement");

            return parts.Count == 0
                ? "pullback context"
                : string.Join(", ", parts);
        }

        private static string BuildCandidateNotes(CandidateSignalSnapshot snapshot)
        {
            var parts = new List<string>();

            if (snapshot.DailyMaDelta3 > 0m)
                parts.Add("daily MA improving");

            if (snapshot.DailyRsiDelta3 > 0m)
                parts.Add("daily RSI improving");

            if (snapshot.DailyMacdDelta3 > 0m)
                parts.Add("daily MACD improving");

            if (snapshot.H4MaDelta3 > 0m)
                parts.Add("H4 MA improving");

            if (snapshot.H4RsiDelta3 > 0m)
                parts.Add("H4 RSI improving");

            if (snapshot.H4MacdDelta3 > 0m)
                parts.Add("H4 MACD improving");

            return parts.Count == 0
                ? "entry confirmed"
                : string.Join(", ", parts);
        }

        private static DateTime GetMarketNow(string timezoneId)
        {
            return MarketTime.Now(timezoneId);
        }

        private sealed class MacdPoint
        {
            public decimal Macd { get; init; }
            public decimal Signal { get; init; }
        }

        private sealed class RecentFeatureSeries
        {
            public List<decimal> DailyCloseSeries { get; init; } = [];
            public List<decimal> DailyOpenSeries { get; init; } = [];
            public List<decimal> DailyHighSeries { get; init; } = [];
            public List<decimal> DailyLowSeries { get; init; } = [];
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
            public List<decimal> H4OpenSeries { get; init; } = [];
            public List<decimal> H4HighSeries { get; init; } = [];
            public List<decimal> H4LowSeries { get; init; } = [];
            public List<decimal> H4CloseSeries { get; init; } = [];
            public List<decimal> H4BbUpperBandSeries { get; init; } = [];
            public List<decimal> H4BbMidBandSeries { get; init; } = [];
            public List<decimal> H4BbLowerBandSeries { get; init; } = [];
            public List<decimal> H4RsiSeries { get; init; } = [];
            public List<decimal> H4MacdLineSeries { get; init; } = [];
            public List<decimal> H4MacdSignalSeries { get; init; } = [];
            public List<decimal> H4MacdHistogramSeries { get; init; } = [];
        }

        private sealed class BollingerStateSet
        {
            public required BollingerStateOutput Weekly { get; init; }
            public required BollingerStateOutput Daily { get; init; }
            public required BollingerStateOutput H4 { get; init; }
        }

        private sealed class BollingerStateOutput
        {
            public string Direction { get; init; } = string.Empty;
            public string Regime { get; init; } = string.Empty;
            public decimal MidSlope { get; init; }
            public decimal WidthSlope { get; init; }
            public decimal UpperDistanceSlope { get; init; }
        }

        private sealed record SeriesEntryProfileDecision(
            decimal? EntryDiscountPct,
            string Profile,
            decimal DailyMidSlope,
            decimal DailyRsiSlope,
            decimal DailyMacdSlope,
            decimal H4MidSlope,
            decimal H4RsiSlope,
            decimal H4MacdSlope);

        private enum SeriesTemplateFamily
        {
            TodayResearchLike,
            Reversal
        }

        private enum DailyFamilySplit
        {
            Unknown,
            TodayResearchLike,
            Reversal
        }

        private enum TodayResearchLikePatternKind
        {
            None,
            Runaway,
            LaunchContinuation,
            BellUp,
            PullbackContinuation
        }

        private readonly record struct DailySplitDiagnostic(
            string Source,
            int DailyBarsCount,
            int CompletedDailyBarsCount,
            DateTime LatestRawDailyBarDate,
            DateTime MarketToday,
            bool TrimmedCurrentDay,
            decimal LatestClosedDailyClose,
            decimal PreviousClosedDailyMid,
            bool IsBelowMid);

        private sealed record CandidateGroupItem(
            CandidateDetails Candidate,
            bool IsSameDay);

        private sealed class ScanPerformanceSummary
        {
            public List<ScanPerformanceStageMetric> Stages { get; } = [];
            public List<ScanPerformanceTickerMetric> Tickers { get; } = [];

            public ScanPerformanceStageMetric BeginStage(string name)
            {
                var metric = new ScanPerformanceStageMetric(name);
                Stages.Add(metric);
                return metric;
            }

            public ScanPerformanceTickerMetric BeginTicker(string stageName, string ticker)
            {
                var stage = Stages.LastOrDefault(x =>
                    string.Equals(x.Name, stageName, StringComparison.OrdinalIgnoreCase));
                var metric = new ScanPerformanceTickerMetric(stageName, ticker, stage);
                Tickers.Add(metric);
                return metric;
            }
        }

        private sealed class ScanPerformanceStageMetric
        {
            private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

            public ScanPerformanceStageMetric(string name)
            {
                Name = name;
            }

            public string Name { get; }
            public int Input { get; set; }
            public int Processed { get; set; }
            public HashSet<string> UniqueTickers { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int HistoricalLoads { get; set; }
            public int CacheHits { get; set; }
            public int Added { get; set; }
            public int Skipped { get; set; }
            public TimeSpan Elapsed { get; private set; }

            public void Stop()
            {
                _stopwatch.Stop();
                Elapsed = _stopwatch.Elapsed;
            }
        }

        private sealed class ScanPerformanceTickerMetric
        {
            private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
            private readonly ScanPerformanceStageMetric? _stage;

            public ScanPerformanceTickerMetric(
                string stageName,
                string ticker,
                ScanPerformanceStageMetric? stage)
            {
                StageName = stageName;
                Ticker = ticker;
                _stage = stage;
            }

            public string StageName { get; }
            public string Ticker { get; }
            public int HistoricalLoads { get; set; }
            public int CacheHits { get; set; }
            public bool Added { get; set; }
            public bool Skipped { get; set; }
            public int Errors { get; set; }
            public TimeSpan Elapsed { get; private set; }

            public void AddHistoricalLoad()
            {
                HistoricalLoads++;

                if (_stage != null)
                    _stage.HistoricalLoads++;
            }

            public void Stop()
            {
                _stopwatch.Stop();
                Elapsed = _stopwatch.Elapsed;
            }
        }

        private sealed class WishListContext
        {
            public required StockInfo Stock { get; init; }
            public Contract? Contract { get; set; }
            public required PresetScanCode Preset { get; init; }
            public required CandidateSignalSnapshot Snapshot { get; init; }
            public required List<Candle> Candles { get; init; }
            public List<Candle>? DailyCandles { get; init; }
            public List<Candle>? ChartH4Candles { get; init; }
            public required DateTime ScanTimeMarket { get; init; }
            public required decimal AvgDollarVolumeDaily { get; init; }
            public required WishListItem WishListItem { get; init; }
            public TradePlanInfo? Trade { get; set; }
            public List<Candle>? EntryCandles { get; set; }
            public DateTime? EntryObservedAt { get; set; }
            public ScanPerformanceTickerMetric? PerformanceMetric { get; set; }
        }
    }
}
