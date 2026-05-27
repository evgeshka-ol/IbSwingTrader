using System.Globalization;
using System.Reflection;
using System.Text;
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
        IWishListFilter wishListFilter,
        IWishListScore wishListScore,
        ICandidateFilter candidateFilter,
        ICandidateScore candidateScore,
        ITradeBuilder tradeBuilder,
        IScanCodeInfoService scannerPresets,
        IWishListReader wishListReader,
        IWishListMerger wishListMerger,
        IAgentPathService pathService,
        IMarketSettingsProvider marketSettingsProvider,
        IGetCandidatesSettingsProvider getCandidatesSettingsProvider,
        IResearchSettingsProvider researchSettingsProvider,
        IEvaluationDatasetCsvService evaluationDatasetCsvService,
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
        private readonly IWishListFilter _wishListFilter = wishListFilter;
        private readonly IWishListScore _wishListScore = wishListScore;
        private readonly ICandidateFilter _candidateFilter = candidateFilter;
        private readonly ICandidateScore _candidateScore = candidateScore;
        private readonly ITradeBuilder _tradeBuilder = tradeBuilder;
        private readonly IScanCodeInfoService _scannerPresets = scannerPresets;
        private readonly IWishListReader _wishListReader = wishListReader;
        private readonly IWishListMerger _wishListMerger = wishListMerger;
        private readonly IAgentPathService _pathService = pathService;
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;
        private readonly IGetCandidatesSettingsProvider _getCandidatesSettingsProvider = getCandidatesSettingsProvider;
        private readonly IResearchSettingsProvider _researchSettingsProvider = researchSettingsProvider;
        private readonly IEvaluationDatasetCsvService _evaluationDatasetCsvService = evaluationDatasetCsvService;
        private readonly INumberTextFormatter _fmt = fmt;
        private readonly ITextLogger _logger = logger;
        private readonly NextDayRankingSettings _nextDayRankingSettings = getCandidatesSettingsProvider.Get().NextDayRanking;
        private const int RecentDailySeriesLength = 12;
        private const int RecentWeeklySeriesLength = 10;
        private const int RecentH4SeriesLength = 16;

        public async Task<CandidateSearchResult> FindAsync()
        {
            var getCandidatesSettings = _getCandidatesSettingsProvider.Get();
            var finderSettings = getCandidatesSettings.Finder;
            var contractResolveTimeout = TimeSpan.FromSeconds(
                Math.Max(15, finderSettings.ContractResolveTimeoutSeconds));
            var contractResolveMaxAttempts = Math.Max(1, finderSettings.ContractResolveMaxAttempts);

            var marketTimezone = _marketSettingsProvider.Get().Timezone;
            var marketNow = GetMarketNow(marketTimezone);
            var todayMarketDate = marketNow.Date;
            var seriesSimilarityTemplates = await LoadSeriesSimilarityTemplatesAsync(
                getCandidatesSettings.NextDayRanking.SeriesSimilarity);

            _logger.Info(
                $"CandidateFinder settings: " +
                $"LookbackCalendarDays={finderSettings.LookbackCalendarDays}, " +
                $"MinimumCandles={finderSettings.MinimumCandles}, " +
                $"AvgVolumePeriod={finderSettings.AvgVolumePeriod}, " +
                $"CandleCount={finderSettings.CandleCount}, " +
                $"ContractResolveTimeoutSeconds={finderSettings.ContractResolveTimeoutSeconds}, " +
                $"ContractResolveMaxAttempts={finderSettings.ContractResolveMaxAttempts}");

            var wishListPath = _pathService.GetWishListFile();
            var currentWishList = await _wishListReader.ReadAsync(wishListPath);

            var scannedWishListContexts = new Dictionary<string, WishListContext>(StringComparer.OrdinalIgnoreCase);
            var candidateResults = new Dictionary<string, CandidateDetails>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in _scannerPresets.GetAll())
            {
                var stocks = await _stockUniverseProvider.GetStocksAsync(preset.ScanCode);

                foreach (var stock in stocks)
                {
                    if (!_preFilter.Pass(stock))
                        continue;

                    Contract? contract = null;
                    List<Candle>? candles;

                    if (!TryLoadPreparedH4CandlesFromCache(stock.Ticker, finderSettings, out candles))
                    {
                        try
                        {
                            contract = await _contractResolver.ResolveStockAsync(
                                stock.Ticker,
                                contractResolveTimeout,
                                contractResolveMaxAttempts);

                            var end = MarketTime.Now();
                            var start = end.AddDays(-finderSettings.LookbackCalendarDays);

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
                            _logger.Info($"Skipping {stock.Ticker}: failed to load candles. {ex.Message}");
                            continue;
                        }
                    }

                    if (candles == null || candles.Count < finderSettings.MinimumCandles)
                    {
                        _logger.Info(
                            $"Skipping {stock.Ticker}: not enough candles " +
                            $"({candles?.Count ?? 0} < {finderSettings.MinimumCandles}).");
                        continue;
                    }

                    var dailyBars = BuildDailyBars(candles);
                    var weeklyBars = BuildWeeklyBars(candles);

                    _logger.Info(
                        $"Ticker history prepared: {stock.Ticker}. " +
                        $"H4={candles.Count}, D1={dailyBars.Count}, W1={weeklyBars.Count}");

                    if (TryRejectByRecentDailyPriceFloor(stock.Ticker, dailyBars, out var recentPriceFloorReason))
                    {
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

                    var weeklyBbRejected = ShouldRejectByWeeklyBbForWishlist(bbState.Weekly);
                    var weeklyBbVetoBypassed = weeklyBbRejected &&
                        ShouldBypassWeeklyBbVetoForLiveMover(
                            stock,
                            preset.ScanCode,
                            snapshot,
                            diagnostics,
                            entryScore);

                    if (weeklyBbRejected && !weeklyBbVetoBypassed)
                    {
                        _logger.Info(
                            $"Wish list BB veto applied: {stock.Ticker}. " +
                            $"Weekly={bbState.Weekly.Regime}/{bbState.Weekly.Direction}");
                        continue;
                    }

                    if (weeklyBbVetoBypassed)
                    {
                        _logger.Info(
                            $"Wish list BB veto bypassed for live mover: {stock.Ticker}. " +
                            $"Preset={preset.ScanCode}, Rank={stock.Rank}, " +
                            $"Weekly={bbState.Weekly.Regime}/{bbState.Weekly.Direction}, " +
                            $"ATRRatio={diagnostics.ATRRatio:0.##}, " +
                            $"DailyRsi14={snapshot.Current.DailyRSI14:0.##}, " +
                            $"EntryScore={entryScore:0.##}");
                    }

                    if (!_wishListFilter.Pass(snapshot, lastPrice, avgDollarVolume))
                    {
                        var weeklyBbBypass = ShouldAllowWeeklyBbWishlistBypass(bbState.Weekly);

                        if (!weeklyBbBypass &&
                            !ShouldBypassWishListFilterForLiveScan(snapshot, diagnostics, entryScore))
                        {
                            _logger.Info($"Wish list rejected: {stock.Ticker}");
                            continue;
                        }

                        if (weeklyBbBypass)
                        {
                            _logger.Info(
                                $"Wish list weekly-BB bypass applied: {stock.Ticker}. " +
                                $"Weekly={bbState.Weekly.Regime}/{bbState.Weekly.Direction}");
                        }
                        else
                        {
                            _logger.Info(
                                $"Wish list live-scan bypass applied: {stock.Ticker}. " +
                                $"EntryScore={_fmt.Generic(entryScore)}, " +
                                $"DailyRsi14={_fmt.Generic(snapshot.Current.DailyRSI14)}, " +
                                $"DailyDistance={_fmt.Generic(snapshot.Current.DailyMaSignedDistancePct)}%, " +
                                $"AtrRatio={_fmt.Generic(diagnostics.ATRRatio)}, " +
                                $"VolumeRatio20={_fmt.Generic(diagnostics.VolumeRatio20)}");
                        }
                    }

                    var wishScore = _wishListScore.Calculate(snapshot);

                    var wishListItem = BuildWishListItem(
                        stock,
                        preset,
                        snapshot,
                        candles,
                        marketNow,
                        marketTimezone,
                        wishScore);

                    AddOrReplaceWishListContext(
                        scannedWishListContexts,
                        new WishListContext
                        {
                            Stock = stock,
                            Contract = contract,
                            Preset = preset,
                            Snapshot = snapshot,
                            Candles = candles,
                            ScanTimeMarket = marketNow,
                            AvgDollarVolumeDaily = avgDollarVolume,
                            WishListItem = wishListItem
                        });
                }
            }

            var scannedWishListItems = scannedWishListContexts.Values
                .Select(x => x.WishListItem)
                .ToList();

            var mergedWishList = _wishListMerger.Merge(currentWishList, scannedWishListItems);
            var forecastedCount = mergedWishList.Count(x => x.ExpectedBarsToTarget != null);

            _logger.Info($"WishList merged. Total={mergedWishList.Count}, WithForecast={forecastedCount}");

            var mergedMap = mergedWishList.ToDictionary(
                x => x.Ticker,
                x => x,
                StringComparer.OrdinalIgnoreCase);

            var agedWishListItems = mergedWishList
                .Where(x =>
                {
                    var firstSeenDate = x.FirstSeen?.Date;
                    return firstSeenDate != null && firstSeenDate.Value < todayMarketDate;
                })
                .OrderByDescending(x => x.Score.Score)
                .ThenByDescending(x => x.LastEvaluatedAt ?? DateTime.MinValue)
                .ToList();

            if (getCandidatesSettings.MaxWishListItems > 0 &&
                agedWishListItems.Count > getCandidatesSettings.MaxWishListItems)
            {
                _logger.Info(
                    $"Aged wish list evaluation limited: taking top {getCandidatesSettings.MaxWishListItems} of {agedWishListItems.Count} items.");

                agedWishListItems = agedWishListItems
                    .Take(getCandidatesSettings.MaxWishListItems)
                    .ToList();
            }

            var sameDayPromotedResults = new Dictionary<string, CandidateDetails>(StringComparer.OrdinalIgnoreCase);

            foreach (var mergedWishItem in agedWishListItems)
            {
                WishListContext? ctx = null;

                if (scannedWishListContexts.TryGetValue(mergedWishItem.Ticker, out var scannedCtx))
                {
                    ctx = scannedCtx;
                }
                else
                {
                    ctx = await TryBuildWishListContextFromExistingItem(
                        mergedWishItem,
                        marketNow,
                        marketTimezone,
                        finderSettings,
                        contractResolveTimeout,
                        contractResolveMaxAttempts);
                }

                if (ctx == null)
                    continue;

                var agedDiagnostics = BuildDiagnostics(ctx.Snapshot, ctx.Candles);
                var agedEntryScore = _candidateScore.Calculate(ctx.Snapshot);
                var agedBbState = BuildBollingerStateSet(BuildRecentFeatureSeries(ctx.Candles));
                var promoteAsTodayResearchLike = IsTodayResearchLikeCandidate(
                    mergedWishItem,
                    ctx,
                    agedDiagnostics,
                    agedEntryScore,
                    agedBbState,
                    seriesSimilarityTemplates);

                if (promoteAsTodayResearchLike)
                {
                    await TryAddCandidate(
                        sameDayPromotedResults,
                        mergedWishItem,
                        ctx,
                        isFromWishlist: false,
                        marketTimezone,
                        bucketName: "today-research-like promoted candidates",
                        rejectionLogPrefix: "Entry rejected after today-research-like promotion",
                        seriesSimilarityTemplates);

                    continue;
                }

                if (!IsReversalCandidateContext(ctx.Snapshot))
                {
                    _logger.Info(
                        $"Skipping aged reversal promotion for {ctx.Stock.Ticker}. " +
                        $"Reversal context was not confirmed and TodayResearchLike conditions were not confirmed.");
                    continue;
                }

                await TryAddCandidate(
                    candidateResults,
                    mergedWishItem,
                    ctx,
                    isFromWishlist: true,
                    marketTimezone,
                    bucketName: "candidates",
                    rejectionLogPrefix: "Entry rejected after wish list pass",
                    seriesSimilarityTemplates);
            }

            var sameDayWishListItems = mergedWishList
                .Where(x =>
                {
                    var firstSeenDate = x.FirstSeen?.Date;
                    return firstSeenDate != null && firstSeenDate.Value == todayMarketDate;
                })
                .OrderByDescending(x => x.Score.Score)
                .ToList();

            foreach (var mergedWishItem in sameDayWishListItems)
            {
                if (!scannedWishListContexts.TryGetValue(mergedWishItem.Ticker, out var ctx))
                    continue;

                var sameDayDiagnostics = BuildDiagnostics(ctx.Snapshot, ctx.Candles);
                var sameDayEntryScore = _candidateScore.Calculate(ctx.Snapshot);
                var sameDayBbState = BuildBollingerStateSet(BuildRecentFeatureSeries(ctx.Candles));
                var promoteAsTodayResearchLike = IsTodayResearchLikeCandidate(
                    mergedWishItem,
                    ctx,
                    sameDayDiagnostics,
                    sameDayEntryScore,
                    sameDayBbState,
                    seriesSimilarityTemplates);

                if (promoteAsTodayResearchLike)
                {
                    await TryAddCandidate(
                        sameDayPromotedResults,
                        mergedWishItem,
                        ctx,
                        isFromWishlist: false,
                        marketTimezone,
                        bucketName: "same-day promoted candidates",
                        rejectionLogPrefix: "Entry rejected after same-day promotion",
                        seriesSimilarityTemplates);
                }
                else if (IsReversalCandidateContext(ctx.Snapshot))
                {
                    await TryAddCandidate(
                        candidateResults,
                        mergedWishItem,
                        ctx,
                        isFromWishlist: true,
                        marketTimezone,
                        bucketName: "same-day reversal candidates",
                        rejectionLogPrefix: "Entry rejected after same-day reversal promotion",
                        seriesSimilarityTemplates);
                }
                else
                {
                    _logger.Info(
                        $"Skipping same-day classification for {ctx.Stock.Ticker}. " +
                        $"Reversal context was not confirmed and TodayResearchLike conditions were not confirmed.");
                }
            }

            foreach (var ctx in scannedWishListContexts.Values)
            {
                if (sameDayPromotedResults.ContainsKey(ctx.Stock.Ticker))
                    continue;

                if (!mergedMap.TryGetValue(ctx.Stock.Ticker, out var mergedWishItem))
                    continue;

                if (ctx.Snapshot.Current.DailyMaSignedDistancePct < 0m)
                    continue;

                var liveDiagnostics = BuildDiagnostics(ctx.Snapshot, ctx.Candles);
                var liveEntryScore = _candidateScore.Calculate(ctx.Snapshot);
                var liveBbState = BuildBollingerStateSet(BuildRecentFeatureSeries(ctx.Candles));
                var promoteAsTodayResearchLike = IsTodayResearchLikeCandidate(
                    mergedWishItem,
                    ctx,
                    liveDiagnostics,
                    liveEntryScore,
                    liveBbState,
                    seriesSimilarityTemplates);

                if (!promoteAsTodayResearchLike)
                    continue;

                _logger.Info(
                    $"Live above-mid TodayResearchLike promotion applied: {ctx.Stock.Ticker}. " +
                    $"Preset={ctx.Preset.ScanCode}, " +
                    $"FirstSeen={mergedWishItem.FirstSeen?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null"}");

                await TryAddCandidate(
                    sameDayPromotedResults,
                    mergedWishItem,
                    ctx,
                    isFromWishlist: false,
                    marketTimezone,
                    bucketName: "live today-research-like candidates",
                    rejectionLogPrefix: "Entry rejected after live today-research-like promotion",
                    seriesSimilarityTemplates);
            }

            if (candidateResults.Count == 0)
            {
                _logger.Info(
                    "No entry candidates from aged wish list items. " +
                    "Trying same-day market-scan fallback.");

                foreach (var ctx in scannedWishListContexts.Values)
                {
                    if (!mergedMap.TryGetValue(ctx.Stock.Ticker, out var mergedWishItem))
                        continue;

                    var firstSeenDate = mergedWishItem.FirstSeen?.Date;

                    if (firstSeenDate != null && firstSeenDate.Value < todayMarketDate)
                        continue;

                    await TryAddCandidate(
                        sameDayPromotedResults,
                        mergedWishItem,
                        ctx,
                        isFromWishlist: false,
                        marketTimezone,
                        bucketName: "fallback candidates",
                        rejectionLogPrefix: "Entry rejected after same-day fallback",
                        seriesSimilarityTemplates);
                }

                _logger.Info($"Same-day market-scan fallback completed. Candidates={sameDayPromotedResults.Count}");
            }

            var promotedTickers = candidateResults.Values
                .Concat(sameDayPromotedResults.Values)
                .Select(x => x.Ticker)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sameDayPromotedTickers = sameDayPromotedResults.Values
                .Select(x => x.Ticker)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var premarketSummaryCandidates = await BuildPremarketSummaryCandidates(
                mergedWishList,
                mergedMap,
                scannedWishListContexts,
                promotedTickers,
                marketNow,
                marketTimezone,
                seriesSimilarityTemplates);

            var sameDayCandidates = sameDayPromotedResults.Values
                .Concat(premarketSummaryCandidates)
                .GroupBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .Select(x => x
                    .OrderByDescending(y => y.Score.NextDayRank ?? decimal.MinValue)
                    .ThenByDescending(y => y.Score.Score)
                    .First())
                .ToList();

            sameDayCandidates = ReRankCandidates(
                sameDayCandidates,
                getCandidatesSettings.PremarketSummary.MaxItems,
                _nextDayRankingSettings,
                seriesSimilarityTemplates,
                SeriesTemplateFamily.TodayResearchLike);

            var finalWishList = mergedWishList
                .Where(x => !promotedTickers.Contains(x.Ticker))
                .ToList();

            var finalForecastedCount = finalWishList.Count(x => x.ExpectedBarsToTarget != null);
            _logger.Info($"WishList final. Total={finalWishList.Count}, WithForecast={finalForecastedCount}");

            var finalCandidates = ReRankCandidates(
                candidateResults.Values
                    .Where(x => !sameDayPromotedTickers.Contains(x.Ticker))
                    .ToList(),
                getCandidatesSettings.FinalTopCandidates,
                _nextDayRankingSettings,
                seriesSimilarityTemplates,
                SeriesTemplateFamily.Reversal);

            return new CandidateSearchResult
            {
                Candidates = [.. finalCandidates],
                SameDayCandidates = sameDayCandidates,
                WishList = finalWishList
            };
        }

        private async Task<List<CandidateDetails>> BuildPremarketSummaryCandidates(
            List<WishListItem> mergedWishList,
            Dictionary<string, WishListItem> mergedMap,
            Dictionary<string, WishListContext> scannedWishListContexts,
            HashSet<string> promotedTickers,
            DateTime marketNow,
            string marketTimezone,
            IReadOnlyList<SeriesSimilarityTemplate> seriesSimilarityTemplates)
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

                var diagnostics = BuildDiagnostics(ctx.Snapshot, ctx.Candles);
                var entryScore = _candidateScore.Calculate(ctx.Snapshot);
                var bbState = BuildBollingerStateSet(BuildRecentFeatureSeries(ctx.Candles));
                var isTodayResearchLikeCandidate = IsTodayResearchLikeCandidate(
                    mergedWishItem,
                    ctx,
                    diagnostics,
                    entryScore,
                    bbState,
                    seriesSimilarityTemplates);

                if (!isTodayResearchLikeCandidate)
                    continue;

                if (entryScore < settings.MinEntryScore && !isTodayResearchLikeCandidate)
                    continue;

                if (!ShouldBypassWishListFilterForLiveScan(ctx.Snapshot, diagnostics, entryScore) &&
                    !isTodayResearchLikeCandidate)
                    continue;

                var trade = ctx.Trade ??= await BuildTradePlan(ctx);
                if (trade.ProfitPercent < settings.MinPlannedProfitPct && !isTodayResearchLikeCandidate)
                    continue;

                var needsDeeperEntry = ResolveNeedsDeeperEntry(ctx.Snapshot, diagnostics);
                var needsMomentumExit = ResolveNeedsMomentumExit(ctx.Snapshot, diagnostics, entryScore);
                var dailyScore = mergedWishItem.Score.DailyScore ?? 0m;
                var weeklyScore = mergedWishItem.Score.WeeklyScore ?? 0m;
                var finalScore = dailyScore + weeklyScore + entryScore;

                var candidateItem = BuildCandidateItem(
                    ctx.Stock,
                    isFromWishlist: false,
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
                    finalScore);

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

                if (itemPriority > existingPriority ||
                    (itemPriority == existingPriority &&
                     item.WishListItem.Score.Score > existing.WishListItem.Score.Score))
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
                        $"ExistingPriority={existingPriority}, NewPriority={itemPriority}");
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
                $"{item.Ticker}|{item.TradePlan.EntryPrice:G29}|{item.TradePlan.ExitPrice:G29}|{item.TradePlan.StopLoss:G29}");
        }

        private async Task TryAddCandidate(
            Dictionary<string, CandidateDetails> candidateResults,
            WishListItem mergedWishItem,
            WishListContext ctx,
            bool isFromWishlist,
            string marketTimezone,
            string bucketName,
            string rejectionLogPrefix,
            IReadOnlyList<SeriesSimilarityTemplate> seriesSimilarityTemplates)
        {
            var diagnostics = BuildDiagnostics(ctx.Snapshot, ctx.Candles);
            var needsDeeperEntry = ResolveNeedsDeeperEntry(ctx.Snapshot, diagnostics);
            var entryScore = _candidateScore.Calculate(ctx.Snapshot);
            var candidateFilterSettings = _getCandidatesSettingsProvider.Get().CandidateFilter;
            var bbState = BuildBollingerStateSet(BuildRecentFeatureSeries(ctx.Candles));
            var isTodayResearchLikeCandidate =
                !isFromWishlist &&
                IsTodayResearchLikeCandidate(
                    mergedWishItem,
                    ctx,
                    diagnostics,
                    entryScore,
                    bbState,
                    seriesSimilarityTemplates);

            if (!_candidateFilter.Pass(ctx.Snapshot, 0m, ctx.AvgDollarVolumeDaily))
            {
                if (isTodayResearchLikeCandidate)
                {
                    _logger.Info(
                        $"TodayResearchLike candidate-filter bypass applied: {ctx.Stock.Ticker}. " +
                        $"W={bbState.Weekly.Regime}/{bbState.Weekly.Direction}, " +
                        $"D={bbState.Daily.Regime}/{bbState.Daily.Direction}, " +
                        $"H4={bbState.H4.Regime}/{bbState.H4.Direction}, " +
                        $"EntryScore={_fmt.Generic(entryScore)}");
                }
                else
                {
                    _logger.Info($"{rejectionLogPrefix}: {ctx.Stock.Ticker}");
                    return;
                }
            }

            var trade = ctx.Trade ??= await BuildTradePlan(ctx);

            if (trade.ProfitPercent < candidateFilterSettings.MinPlannedProfitPct)
            {
                if (isTodayResearchLikeCandidate)
                {
                    _logger.Info(
                        $"TodayResearchLike low-profit override applied: {ctx.Stock.Ticker}. " +
                        $"ProfitPercent={_fmt.Percent(trade.ProfitPercent)}%, " +
                        $"MinRequired={_fmt.Percent(candidateFilterSettings.MinPlannedProfitPct)}%");
                }
                else
                {
                    _logger.Info(
                        $"{rejectionLogPrefix}: {ctx.Stock.Ticker}. " +
                        $"Planned profit is too small: ProfitPercent={_fmt.Percent(trade.ProfitPercent)}%, " +
                        $"MinRequired={_fmt.Percent(candidateFilterSettings.MinPlannedProfitPct)}%");
                    return;
                }
            }

            var dailyScore = mergedWishItem.Score.DailyScore ?? 0m;
            var weeklyScore = mergedWishItem.Score.WeeklyScore ?? 0m;
            var finalScore = dailyScore + weeklyScore + entryScore;

            var needsMomentumExit = ResolveNeedsMomentumExit(ctx.Snapshot, diagnostics, entryScore);

            var candidateItem = BuildCandidateItem(
                ctx.Stock,
                isFromWishlist,
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
                finalScore);

            _logger.Info(
                $"BB regimes for {ctx.Stock.Ticker}: " +
                $"W={candidateItem.WeeklyBbRegime}/{candidateItem.WeeklyBbDirection} " +
                $"(mid={_fmt.Generic(candidateItem.WeeklyBbMidSlope)}, width={_fmt.Generic(candidateItem.WeeklyBbWidthSlope)}, upper={_fmt.Generic(candidateItem.WeeklyBbUpperDistanceSlope)}), " +
                $"D={candidateItem.DailyBbRegime}/{candidateItem.DailyBbDirection} " +
                $"(mid={_fmt.Generic(candidateItem.DailyBbMidSlope)}, width={_fmt.Generic(candidateItem.DailyBbWidthSlope)}, upper={_fmt.Generic(candidateItem.DailyBbUpperDistanceSlope)}), " +
                $"H4={candidateItem.H4BbRegime}/{candidateItem.H4BbDirection} " +
                $"(mid={_fmt.Generic(candidateItem.H4BbMidSlope)}, width={_fmt.Generic(candidateItem.H4BbWidthSlope)}, upper={_fmt.Generic(candidateItem.H4BbUpperDistanceSlope)})");

            AddOrReplaceHigherScore(candidateResults, candidateItem, bucketName);
        }

        private bool IsTodayResearchLikeCandidate(
            WishListItem mergedWishItem,
            WishListContext ctx,
            CandidateDiagnostics diagnostics,
            decimal entryScore,
            BollingerStateSet bbState,
            IReadOnlyList<SeriesSimilarityTemplate> seriesSimilarityTemplates)
        {
            var firstSeenDate = mergedWishItem.FirstSeen?.Date;
            if (ShouldRejectByWeeklyBbForWishlist(bbState.Weekly) &&
                !ShouldBypassWeeklyBbVetoForLiveMover(
                    ctx.Stock,
                    ctx.Preset.ScanCode,
                    ctx.Snapshot,
                    diagnostics,
                    entryScore) &&
                !ShouldBypassAgedWeeklyBbVeto(
                    mergedWishItem,
                    ctx.Snapshot,
                    diagnostics,
                    entryScore))
            {
                return false;
            }

            var recentSeries = BuildRecentFeatureSeries(ctx.Candles);
            var preLaunchResearchLike = IsTodayResearchLikePreLaunchCandidate(
                mergedWishItem,
                ctx,
                diagnostics,
                bbState,
                recentSeries);
            var shortHistoryLiveMover = IsShortHistoryLiveMoverCandidate(
                ctx,
                diagnostics,
                recentSeries);
            var mixedMeanLiveWinner = IsMixedMeanLiveWinnerCandidate(
                ctx,
                diagnostics,
                entryScore);
            var strongLiveMove = ShouldBypassWishListFilterForLiveScan(ctx.Snapshot, diagnostics, entryScore);
            var currentSessionLikeMove =
                string.Equals(ctx.Preset.ScanCode, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ctx.Preset.ScanCode, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ctx.Preset.ScanCode, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ctx.Preset.ScanCode, "TOP_OPEN_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                strongLiveMove;
            var seriesTemplateMatch = CalculateSeriesSimilarityMatch(
                recentSeries,
                seriesSimilarityTemplates,
                SeriesTemplateFamily.TodayResearchLike,
                _nextDayRankingSettings.SeriesSimilarity);
            var seriesTemplatePromotion =
                currentSessionLikeMove &&
                seriesTemplateMatch.Bonus > 0m &&
                (entryScore >= 15m || diagnostics.ATRRatio >= 3m || ctx.Stock.Rank <= 20);

            if (ctx.Snapshot.Current.DailyMaSignedDistancePct < 0m &&
                !preLaunchResearchLike &&
                !shortHistoryLiveMover &&
                !mixedMeanLiveWinner &&
                !seriesTemplatePromotion)
            {
                return false;
            }

            var constructiveWeekly =
                string.Equals(bbState.Weekly.Direction, nameof(BollingerFigureDirection.Up), StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(bbState.Weekly.Regime, nameof(BollingerFigureRegime.Runaway), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(bbState.Weekly.Regime, nameof(BollingerFigureRegime.Pullback), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(bbState.Weekly.Regime, nameof(BollingerFigureRegime.Reacceleration), StringComparison.OrdinalIgnoreCase));

            var actionableDaily =
                string.Equals(bbState.Daily.Direction, nameof(BollingerFigureDirection.Up), StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(bbState.Daily.Regime, nameof(BollingerFigureRegime.Runaway), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(bbState.Daily.Regime, nameof(BollingerFigureRegime.Pullback), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(bbState.Daily.Regime, nameof(BollingerFigureRegime.Collapse), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(bbState.Daily.Regime, nameof(BollingerFigureRegime.Reacceleration), StringComparison.OrdinalIgnoreCase));

            var h4Supportive =
                string.Equals(bbState.H4.Direction, nameof(BollingerFigureDirection.Up), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bbState.H4.Regime, nameof(BollingerFigureRegime.Pullback), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bbState.H4.Regime, nameof(BollingerFigureRegime.Runaway), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bbState.H4.Regime, nameof(BollingerFigureRegime.Reacceleration), StringComparison.OrdinalIgnoreCase);

            var strongRunawayUp = IsStrongRunawayUp(ctx.Snapshot, diagnostics, bbState);
            var strongSeriesRunawayUp = IsStrongTodayResearchLikeSeries(recentSeries);
            var runawaySeriesScore = CalculateTodayResearchLikeSeriesScore(recentSeries);
            var weeklyDistance = ctx.Snapshot.Current.WeeklyMaSignedDistancePct ?? 0m;
            var dailyDistance = ctx.Snapshot.Current.DailyMaSignedDistancePct;
            var h4Distance = ctx.Snapshot.Current.H4MaSignedDistancePct;
            var dailyMaDelta3 = ctx.Snapshot.DailyMaDelta3;
            var h4MaDelta3 = ctx.Snapshot.H4MaDelta3;
            var dailyRsiDelta3 = ctx.Snapshot.DailyRsiDelta3;
            var dailyMacdDelta3 = ctx.Snapshot.DailyMacdDelta3;
            var weeklyMidLast = recentSeries.WeeklyBbMidDistanceSeries.LastOrDefault();
            var dailyMidLast = recentSeries.DailyBbMidDistanceSeries.LastOrDefault();
            var h4MidLast = recentSeries.H4BbMidDistanceSeries.LastOrDefault();
            var weeklyMacdLast = recentSeries.WeeklyMacdSeries.LastOrDefault();
            var dailyMacdLast = recentSeries.DailyMacdSeries.LastOrDefault();
            var h4MacdLast = recentSeries.H4MacdSeries.LastOrDefault();
            var weeklyMidSlope = CalculateSlope(recentSeries.WeeklyBbMidDistanceSeries);
            var dailyMidSlope = CalculateSlope(recentSeries.DailyBbMidDistanceSeries);
            var h4MidSlope = CalculateSlope(recentSeries.H4BbMidDistanceSeries);
            var weeklyWidthSlope = CalculateSlope(recentSeries.WeeklyBbWidthSeries);
            var dailyWidthSlope = CalculateSlope(recentSeries.DailyBbWidthSeries);
            var h4WidthSlope = CalculateSlope(recentSeries.H4BbWidthSeries);

            var weeklySeriesConstructive =
                weeklyMidLast > 0m &&
                weeklyMacdLast > -0.35m &&
                weeklyMidSlope > -20m &&
                weeklyWidthSlope > -80m;

            var dailySeriesConstructive =
                dailyMidLast > 10m &&
                dailyMacdLast > -0.20m &&
                dailyMidSlope > -25m &&
                dailyWidthSlope > -60m;

            var h4SeriesSupportive =
                h4MidLast > 5m &&
                h4MacdLast > -0.25m &&
                h4MidSlope > -30m &&
                h4WidthSlope > -60m;

            var seriesDrivenTodayResearchLike =
                weeklySeriesConstructive &&
                dailySeriesConstructive &&
                h4SeriesSupportive &&
                runawaySeriesScore >= 10m;

            var liveSeriesPromotion =
                currentSessionLikeMove &&
                weeklyMidLast > 0m &&
                dailyMidLast > 8m &&
                h4MidLast > 0m &&
                weeklyMacdLast > -0.35m &&
                dailyMacdLast > -0.10m &&
                h4MacdLast > -0.10m &&
                runawaySeriesScore >= 8m;

            var liveSnapshotPromotion =
                currentSessionLikeMove &&
                weeklyDistance > 25m &&
                dailyDistance > 15m &&
                h4Distance > 2m &&
                dailyMaDelta3 > 0m &&
                h4MaDelta3 > 0m &&
                dailyRsiDelta3 > -2m &&
                dailyMacdDelta3 > -0.05m;

            var earlyRunawayCoolingPromotion =
                currentSessionLikeMove &&
                weeklyDistance > 20m &&
                dailyDistance > 10m &&
                h4Distance > 0m &&
                weeklyMacdLast > 0m &&
                dailyMacdLast > 0m &&
                h4MacdLast > -0.10m &&
                dailyMaDelta3 > -12m &&
                h4MaDelta3 > -14m &&
                dailyRsiDelta3 > -4m &&
                dailyMacdDelta3 > -0.08m &&
                runawaySeriesScore >= 8m;

            var staleLiveRunaway =
                currentSessionLikeMove &&
                weeklyDistance > 80m &&
                dailyDistance > 50m &&
                dailyMaDelta3 < -8m &&
                h4MaDelta3 < -8m &&
                dailyRsiDelta3 < -4m;

            var bornToday = firstSeenDate == ctx.ScanTimeMarket.Date;
            var canIgnoreForecastGate =
                (currentSessionLikeMove || strongRunawayUp || strongSeriesRunawayUp || runawaySeriesScore >= 9m || liveSnapshotPromotion) &&
                (constructiveWeekly || weeklySeriesConstructive) &&
                (actionableDaily || dailySeriesConstructive) &&
                (h4Supportive || h4SeriesSupportive);

            if (staleLiveRunaway)
                return false;

            if (preLaunchResearchLike)
            {
                _logger.Info(
                    $"TodayResearchLike pre-launch promotion applied: {ctx.Stock.Ticker}. " +
                    $"Preset={ctx.Preset.ScanCode}, " +
                    $"DailyDistance={_fmt.Generic(dailyDistance)}%, " +
                    $"DistanceTo20dHigh={_fmt.Generic(ctx.Snapshot.Current.DistanceTo20dHigh)}%, " +
                    $"DailyRsi14={_fmt.Generic(ctx.Snapshot.Current.DailyRSI14)}, " +
                    $"Score={_fmt.Generic(mergedWishItem.Score.Score)}");
                return true;
            }

            if (shortHistoryLiveMover)
            {
                _logger.Info(
                    $"TodayResearchLike short-history live-mover promotion applied: {ctx.Stock.Ticker}. " +
                    $"Preset={ctx.Preset.ScanCode}, " +
                    $"DailyDistance={_fmt.Generic(dailyDistance)}%, " +
                    $"H4Distance={_fmt.Generic(h4Distance)}%, " +
                    $"DistanceTo20dHigh={_fmt.Generic(ctx.Snapshot.Current.DistanceTo20dHigh)}%, " +
                    $"DailyRsi14={_fmt.Generic(ctx.Snapshot.Current.DailyRSI14)}, " +
                    $"AtrRatio={_fmt.Generic(diagnostics.ATRRatio)}");
                return true;
            }

            if (mixedMeanLiveWinner)
            {
                _logger.Info(
                    $"TodayResearchLike mixed-mean live-winner promotion applied: {ctx.Stock.Ticker}. " +
                    $"Preset={ctx.Preset.ScanCode}, " +
                    $"Rank={ctx.Stock.Rank}, " +
                    $"WeeklyDistance={_fmt.Generic(weeklyDistance)}%, " +
                    $"DailyDistance={_fmt.Generic(dailyDistance)}%, " +
                    $"H4Distance={_fmt.Generic(h4Distance)}%, " +
                    $"DailyRsi14={_fmt.Generic(ctx.Snapshot.Current.DailyRSI14)}, " +
                    $"AtrRatio={_fmt.Generic(diagnostics.ATRRatio)}, " +
                    $"EntryScore={_fmt.Generic(entryScore)}");
                return true;
            }

            if (seriesTemplatePromotion)
            {
                _logger.Info(
                    $"TodayResearchLike series-template promotion applied: {ctx.Stock.Ticker}. " +
                    $"Preset={ctx.Preset.ScanCode}, " +
                    $"Template={seriesTemplateMatch.TemplateTicker}, " +
                    $"Distance={_fmt.Generic(seriesTemplateMatch.TotalDistance ?? 0m)}, " +
                    $"Daily={_fmt.Generic(seriesTemplateMatch.DailyDistance ?? 0m)}, " +
                    $"Weekly={_fmt.Generic(seriesTemplateMatch.WeeklyDistance ?? 0m)}, " +
                    $"H4={_fmt.Generic(seriesTemplateMatch.H4Distance ?? 0m)}, " +
                    $"Bonus={_fmt.Generic(seriesTemplateMatch.Bonus)}, " +
                    $"EntryScore={_fmt.Generic(entryScore)}, " +
                    $"AtrRatio={_fmt.Generic(diagnostics.ATRRatio)}");
                return true;
            }

            if (seriesDrivenTodayResearchLike || liveSeriesPromotion || liveSnapshotPromotion || earlyRunawayCoolingPromotion)
                return true;

            if (!canIgnoreForecastGate &&
                mergedWishItem.ExpectedBarsToTarget != null &&
                mergedWishItem.ExpectedBarsToTarget > 0)
                return false;

            return
                runawaySeriesScore >= 12m ||
                seriesDrivenTodayResearchLike ||
                liveSeriesPromotion ||
                liveSnapshotPromotion ||
                earlyRunawayCoolingPromotion ||
                seriesTemplatePromotion ||
                (bornToday && (strongLiveMove || strongRunawayUp || strongSeriesRunawayUp || runawaySeriesScore >= 9m || liveSnapshotPromotion)) ||
                canIgnoreForecastGate;
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
                ctx.Snapshot.DailyMaDelta3 >= -12m &&
                ctx.Snapshot.DailyRsiDelta3 >= -5m &&
                ctx.Snapshot.H4MaDelta3 >= -14m;

            var h4NotBreakingDown =
                recentSeries.H4BbMidDistanceSeries.LastOrDefault() > -20m &&
                recentSeries.H4MacdSeries.LastOrDefault() > -0.65m;

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
            decimal entryScore)
        {
            if (!IsLiveMoverPreset(ctx.Preset.ScanCode))
                return false;

            var rank = ctx.Stock.Rank > 0 ? ctx.Stock.Rank : int.MaxValue;
            if (rank > 10)
                return false;

            var current = ctx.Snapshot.Current;
            var weeklyDistance = current.WeeklyMaSignedDistancePct ?? 0m;

            return weeklyDistance > 0m &&
                   current.DailyMaSignedDistancePct >= -8m &&
                   current.H4MaSignedDistancePct >= -12m &&
                   (ctx.Snapshot.DailyMaDelta3 > 0m ||
                    ctx.Snapshot.H4MaDelta3 > 0m ||
                    entryScore >= 20m) &&
                   (diagnostics.ATRRatio >= 3m ||
                    current.DailyRSI14 >= 50m ||
                    entryScore >= 20m);
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
                recentSeries.WeeklyBbMidDistanceSeries.Count >= 3 &&
                recentSeries.WeeklyBbWidthSeries.Count >= 3;

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
                ctx.Snapshot.DailyMaDelta3 >= -2m ||
                ctx.Snapshot.DailyRsiDelta3 >= 8m ||
                dailyDistance >= 10m ||
                diagnostics.ATRRatio >= 4m;

            var h4Confirming =
                ctx.Snapshot.H4MaDelta3 >= -8m ||
                h4Distance >= 0m ||
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

        private bool IsStrongTodayResearchLikeSeries(RecentFeatureSeries recentSeries)
        {
            return CalculateTodayResearchLikeSeriesScore(recentSeries) >= 10m;
        }

        private decimal CalculateTodayResearchLikeSeriesScore(RecentFeatureSeries recentSeries)
        {
            var dailyMidLast = recentSeries.DailyBbMidDistanceSeries.LastOrDefault();
            var weeklyMidLast = recentSeries.WeeklyBbMidDistanceSeries.LastOrDefault();
            var h4MidLast = recentSeries.H4BbMidDistanceSeries.LastOrDefault();
            var dailyMacdLast = recentSeries.DailyMacdSeries.LastOrDefault();
            var weeklyMacdLast = recentSeries.WeeklyMacdSeries.LastOrDefault();
            var h4MacdLast = recentSeries.H4MacdSeries.LastOrDefault();

            var dailyMidSlope = CalculateSlope(recentSeries.DailyBbMidDistanceSeries);
            var weeklyMidSlope = CalculateSlope(recentSeries.WeeklyBbMidDistanceSeries);
            var h4MidSlope = CalculateSlope(recentSeries.H4BbMidDistanceSeries);
            var dailyWidthSlope = CalculateSlope(recentSeries.DailyBbWidthSeries);
            var weeklyWidthSlope = CalculateSlope(recentSeries.WeeklyBbWidthSeries);
            var h4WidthSlope = CalculateSlope(recentSeries.H4BbWidthSeries);
            var dailyMacdSlope = CalculateSlope(recentSeries.DailyMacdSeries);
            var weeklyMacdSlope = CalculateSlope(recentSeries.WeeklyMacdSeries);
            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdSeries);

            decimal score = 0m;

            if (dailyMidLast > 15m)
                score += 2m;
            if (dailyMidLast > 35m)
                score += 1m;

            if (weeklyMidLast > 0m)
                score += 1m;
            if (weeklyMidLast > 20m)
                score += 2m;

            if (h4MidLast > 8m)
                score += 1m;
            if (h4MidLast > 20m)
                score += 1m;

            if (dailyMidSlope > -5m)
                score += 1m;
            if (weeklyMidSlope > -5m)
                score += 1m;
            if (h4MidSlope > -10m)
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

            if (dailyWidthSlope > -30m)
                score += 1m;
            if (weeklyWidthSlope > -40m)
                score += 1m;
            if (h4WidthSlope > -30m)
                score += 1m;

            return score;
        }

        private static bool IsReversalCandidateContext(CandidateSignalSnapshot snapshot)
        {
            return snapshot.Current.DailyMaSignedDistancePct < 0m &&
                   (snapshot.Current.WeeklyMaSignedDistancePct ?? 0m) < 0m;
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

            var dailyBars = BuildDailyBars(candles);
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
                ScanTimeMarket = marketNow,
                AvgDollarVolumeDaily = avgDollarVolume,
                WishListItem = item
            };
        }

        private async Task<TradePlanInfo> BuildTradePlan(WishListContext ctx)
        {
            var tradeSettings = _getCandidatesSettingsProvider.Get().TradePlan;
            var finderSettings = _getCandidatesSettingsProvider.Get().Finder;
            List<Candle>? entryCandles = null;

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

            if (ctx.Contract != null)
            {
                try
                {
                    var end = MarketTime.Now();
                    var start = end.AddHours(-tradeSettings.EntryLookbackHours);

                    entryCandles = await _historicalData.GetCandlesRange(
                        ctx.Stock.Ticker,
                        ctx.Contract,
                        Timeframe.M15,
                        start,
                        end);
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
            var isResearchLikeLaunch = IsResearchLikeLaunch(
                ctx.Snapshot,
                diagnostics,
                recentSeries,
                _nextDayRankingSettings);
            var isResearchLikeReadyNow = IsResearchLikeReadyNow(recentSeries, tradeSettings.ResearchLikeExit);

            decimal? defaultProfitPctOverride = momentumExit?.DefaultProfitPct;
            decimal? minProfitPctOverride = momentumExit?.MinProfitPct;
            decimal? maxProfitPctOverride = momentumExit?.MaxProfitPct;
            decimal? maxLossPctOverride = null;

            if (isParabolicExpansion)
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
                if (isResearchLikeReadyNow)
                {
                    defaultProfitPctOverride = researchLikeSettings.DefaultProfitPct;
                    minProfitPctOverride = researchLikeSettings.MinProfitPct;
                    maxProfitPctOverride = researchLikeSettings.MaxProfitPct;
                    entryDiscountOverridePct ??= researchLikeSettings.EntryDiscountPct;

                    _logger.Info(
                        $"Trade plan research-like ready-now profile applied for {ctx.Stock.Ticker}. " +
                        $"EntryDiscountPct={_fmt.Percent(entryDiscountOverridePct ?? 0m)}, " +
                        $"DefaultProfitPct={_fmt.Percent(researchLikeSettings.DefaultProfitPct)}, " +
                        $"MinProfitPct={_fmt.Percent(researchLikeSettings.MinProfitPct)}, " +
                        $"MaxProfitPct={_fmt.Percent(researchLikeSettings.MaxProfitPct)}");
                }
                else
                {
                    entryDiscountOverridePct ??= researchLikeSettings.EarlyEntryDiscountPct;

                    _logger.Info(
                        $"Trade plan research-like early-entry profile applied for {ctx.Stock.Ticker}. " +
                        $"EntryDiscountPct={_fmt.Percent(entryDiscountOverridePct ?? 0m)}");
                }
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
                recentSeries,
                tradeSettings.H4BollingerEntry,
                entryDiscountOverridePct);

            if (bbEntryDiscountOverridePct != entryDiscountOverridePct)
            {
                _logger.Info(
                    $"Trade plan H4 BB entry adjustment applied for {ctx.Stock.Ticker}. " +
                    $"W={bbState.Weekly.Regime}/{bbState.Weekly.Direction}, " +
                    $"D={bbState.Daily.Regime}/{bbState.Daily.Direction}, " +
                    $"H4={bbState.H4.Regime}/{bbState.H4.Direction}, " +
                    $"EntryDiscountPct={_fmt.Percent(bbEntryDiscountOverridePct ?? 0m)}, " +
                    $"H4MidDistance={_fmt.Percent(GetLatestSignedPercent(recentSeries.H4BbMidDistanceSeries))}, " +
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

            var trade = _tradeBuilder.Build(
                ctx.Candles,
                entryCandles,
                entryDiscountOverridePct,
                defaultProfitPctOverride,
                minProfitPctOverride,
                maxProfitPctOverride,
                maxLossPctOverride);

            return new TradePlanInfo
            {
                EntryPrice = trade.Entry,
                ExitPrice = trade.Exit,
                StopLoss = trade.Stop,
                StopLimitPrice = trade.StopLimit,
                ProfitPercent = CalculatePercent(trade.Entry, trade.Exit),
                LossPercent = CalculatePercent(trade.Entry, trade.Stop),
                ExitProfile = trade.ExitProfile
            };
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
                $"WishList item build started: {stock.Ticker}. " +
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
                $"WishList item build completed: {stock.Ticker}. " +
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
                FirstSeen = scanTimeMarket,
                LastEvaluatedAt = scanTimeMarket,
                ExpectedTargetTime = targetForecast.ExpectedTargetMarketTime,
                ExpectedBarsToTarget = targetForecast.ExpectedBarsToTarget
            };
        }

        private CandidateDetails BuildCandidateItem(
            StockInfo stock,
            bool isFromWishlist,
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
            decimal finalScore)
        {
            var recentSeries = BuildRecentFeatureSeries(candles);
            var bbState = BuildBollingerStateSet(recentSeries);

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
                bbState);

            nextDayRank += CalculateTodayResearchLikeLowProfitRankCompensation(
                preset.ScanCode,
                snapshot,
                recentSeries,
                trade);

            return new CandidateDetails
            {
                Ticker = stock.Ticker,
                IsFromWishlist = isFromWishlist,
                RecentDailyMaSeries = recentSeries.DailyMaSeries,
                RecentDailyBbMidDistanceSeries = recentSeries.DailyBbMidDistanceSeries,
                RecentDailyBbUpperDistanceSeries = recentSeries.DailyBbUpperDistanceSeries,
                RecentDailyBbWidthSeries = recentSeries.DailyBbWidthSeries,
                RecentDailyRsiSeries = recentSeries.DailyRsiSeries,
                RecentDailyMacdSeries = recentSeries.DailyMacdSeries,
                RecentWeeklyMaSeries = recentSeries.WeeklyMaSeries,
                RecentWeeklyBbMidDistanceSeries = recentSeries.WeeklyBbMidDistanceSeries,
                RecentWeeklyBbUpperDistanceSeries = recentSeries.WeeklyBbUpperDistanceSeries,
                RecentWeeklyBbWidthSeries = recentSeries.WeeklyBbWidthSeries,
                RecentWeeklyRsiSeries = recentSeries.WeeklyRsiSeries,
                RecentWeeklyMacdSeries = recentSeries.WeeklyMacdSeries,
                RecentH4MaSeries = recentSeries.H4MaSeries,
                RecentH4BbMidDistanceSeries = recentSeries.H4BbMidDistanceSeries,
                RecentH4BbUpperDistanceSeries = recentSeries.H4BbUpperDistanceSeries,
                RecentH4BbWidthSeries = recentSeries.H4BbWidthSeries,
                RecentH4RsiSeries = recentSeries.H4RsiSeries,
                RecentH4MacdSeries = recentSeries.H4MacdSeries,
                WeeklyBbDirection = bbState.Weekly.Direction,
                WeeklyBbRegime = bbState.Weekly.Regime,
                WeeklyBbMidSlope = bbState.Weekly.MidSlope,
                WeeklyBbWidthSlope = bbState.Weekly.WidthSlope,
                WeeklyBbUpperDistanceSlope = bbState.Weekly.UpperDistanceSlope,
                DailyBbDirection = bbState.Daily.Direction,
                DailyBbRegime = bbState.Daily.Regime,
                DailyBbMidSlope = bbState.Daily.MidSlope,
                DailyBbWidthSlope = bbState.Daily.WidthSlope,
                DailyBbUpperDistanceSlope = bbState.Daily.UpperDistanceSlope,
                H4BbDirection = bbState.H4.Direction,
                H4BbRegime = bbState.H4.Regime,
                H4BbMidSlope = bbState.H4.MidSlope,
                H4BbWidthSlope = bbState.H4.WidthSlope,
                H4BbUpperDistanceSlope = bbState.H4.UpperDistanceSlope,
                NeedsDeeperEntry = needsDeeperEntry,
                NeedsMomentumExit = needsMomentumExit,
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
            RecentFeatureSeries recentSeries,
            TradePlanInfo trade)
        {
            if (snapshot.Current.DailyMaSignedDistancePct < 0m)
                return 0m;

            var minPlannedProfitPct = _getCandidatesSettingsProvider.Get().CandidateFilter.MinPlannedProfitPct;
            if (trade.ProfitPercent >= minPlannedProfitPct)
                return 0m;

            var seriesScore = CalculateTodayResearchLikeSeriesScore(recentSeries);
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

            var dailyMidSlope = CalculateSlope(recentSeries.DailyBbMidDistanceSeries);
            var h4MidSlope = CalculateSlope(recentSeries.H4BbMidDistanceSeries);
            var dailyMacdSlope = CalculateSlope(recentSeries.DailyMacdSeries);
            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdSeries);

            if (dailyMidSlope > -12m && h4MidSlope > -12m)
                score += 0.10m;

            if (dailyMacdSlope > -0.20m && h4MacdSlope > -0.15m)
                score += 0.10m;

            return score;
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
            BollingerStateSet bbState)
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

            score += CalculatePatternSeriesAdjustment(recentSeries, s);

            if (IsResearchLikeLaunch(snapshot, diagnostics, recentSeries, s))
            {
                score += s.ResearchLikeBonus;

                if (HasPositiveSlope(recentSeries.DailyMacdSeries, s.ResearchLikeDailyMacdSlopeThreshold) &&
                    HasPositiveSlope(recentSeries.H4RsiSeries, s.PatternH4RsiSlopeThreshold))
                {
                    score += s.ResearchLikeStrongPatternBonus;
                }

                if (HasPositiveSlope(recentSeries.H4MacdSeries, 0.05m) &&
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

            score += CalculateTodayResearchLikeFreshnessAdjustment(
                presetScanCode,
                snapshot,
                recentSeries);

            return decimal.Round(score, 4, MidpointRounding.AwayFromZero);
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

            var h4MidLast = recentSeries.H4BbMidDistanceSeries.LastOrDefault();
            var h4MacdLast = recentSeries.H4MacdSeries.LastOrDefault();
            var dailyMacdLast = recentSeries.DailyMacdSeries.LastOrDefault();
            var dailyMidSlope = CalculateSlope(recentSeries.DailyBbMidDistanceSeries);
            var h4MidSlope = CalculateSlope(recentSeries.H4BbMidDistanceSeries);

            var dailyTryingToTurn =
                snapshot.DailyMaDelta3 >= -12m &&
                snapshot.DailyRsiDelta3 >= -5m &&
                snapshot.H4MaDelta3 >= -14m &&
                dailyMacdLast > -0.75m;

            var h4NotBreakingDown =
                h4MidLast > -20m &&
                h4MacdLast > -0.65m &&
                dailyMidSlope > -35m &&
                h4MidSlope > -35m;

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
                recentSeries.WeeklyBbMidDistanceSeries.Count >= 3 &&
                recentSeries.WeeklyBbWidthSeries.Count >= 3;

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
                snapshot.DailyMaDelta3 >= -2m ||
                snapshot.DailyRsiDelta3 >= 8m ||
                dailyDistance >= 10m ||
                diagnostics.ATRRatio >= 4m;

            var h4Confirming =
                snapshot.H4MaDelta3 >= -8m ||
                h4Distance >= 0m ||
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
            if (snapshot.Current.DailyMaSignedDistancePct < 0m)
                return 0m;

            var livePreset =
                string.Equals(presetScanCode, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "TOP_OPEN_PERC_GAIN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetScanCode, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase);

            if (!livePreset)
                return 0m;

            var dailyMidLast = recentSeries.DailyBbMidDistanceSeries.LastOrDefault();
            var weeklyMidLast = recentSeries.WeeklyBbMidDistanceSeries.LastOrDefault();
            var h4MidLast = recentSeries.H4BbMidDistanceSeries.LastOrDefault();
            var weeklyMacdLast = recentSeries.WeeklyMacdSeries.LastOrDefault();
            var dailyMacdLast = recentSeries.DailyMacdSeries.LastOrDefault();
            var h4MacdLast = recentSeries.H4MacdSeries.LastOrDefault();

            var weeklyMidSlope = CalculateSlope(recentSeries.WeeklyBbMidDistanceSeries);
            var dailyMidSlope = CalculateSlope(recentSeries.DailyBbMidDistanceSeries);
            var h4MidSlope = CalculateSlope(recentSeries.H4BbMidDistanceSeries);
            var weeklyMacdSlope = CalculateSlope(recentSeries.WeeklyMacdSeries);
            var dailyMacdSlope = CalculateSlope(recentSeries.DailyMacdSeries);
            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdSeries);
            var weeklyWidthSlope = CalculateSlope(recentSeries.WeeklyBbWidthSeries);
            var dailyWidthSlope = CalculateSlope(recentSeries.DailyBbWidthSeries);
            var h4WidthSlope = CalculateSlope(recentSeries.H4BbWidthSeries);
            var topPercGain = string.Equals(presetScanCode, "TOP_PERC_GAIN", StringComparison.OrdinalIgnoreCase);
            var hotByVolume = string.Equals(presetScanCode, "HOT_BY_VOLUME", StringComparison.OrdinalIgnoreCase);
            var mostActive = string.Equals(presetScanCode, "MOST_ACTIVE", StringComparison.OrdinalIgnoreCase);

            decimal score = 0m;

            if (weeklyMidLast > 0m)
                score += 0.15m;
            if (dailyMidLast > 10m)
                score += 0.20m;
            if (dailyMidLast > 30m)
                score += 0.20m;
            if (h4MidLast > 5m)
                score += 0.15m;

            if (dailyMidSlope > 0m)
                score += 0.30m;
            else if (dailyMidSlope < -15m)
                score -= 0.35m;

            if (h4MidSlope > 0m)
                score += 0.25m;
            else if (h4MidSlope < -15m)
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

            if (dailyWidthSlope > -20m)
                score += 0.10m;
            if (h4WidthSlope > -20m)
                score += 0.10m;

            if (snapshot.Current.DailyMaSignedDistancePct > 60m &&
                dailyMidSlope < -10m &&
                h4MidSlope < -10m)
            {
                score -= 0.40m;
            }

            if (snapshot.Current.DailyMaSignedDistancePct > 90m &&
                weeklyMidLast > 40m &&
                dailyMidSlope < -4m &&
                h4MidSlope < -4m &&
                dailyMacdSlope <= 0m &&
                h4MacdSlope <= 0m)
            {
                score -= 0.55m;
            }

            var moderateRunaway =
                weeklyMidLast > 20m &&
                dailyMidLast > 15m &&
                dailyMidLast < 60m &&
                h4MidLast > 5m &&
                dailyMacdLast > 0m &&
                h4MacdLast > -0.10m &&
                dailyMidSlope > -12m &&
                h4MidSlope > -12m;

            if (moderateRunaway)
                score += 0.35m;

            if (moderateRunaway && hotByVolume)
                score += 0.15m;

            if (moderateRunaway && topPercGain)
                score += 0.15m;

            if (moderateRunaway && mostActive)
                score += 0.10m;

            var earlyRunawayStillAlive =
                weeklyMidLast > 10m &&
                dailyMidLast > 10m &&
                h4MidLast > 0m &&
                dailyMacdLast >= 0m &&
                h4MacdLast > -0.10m &&
                dailyWidthSlope > -35m &&
                h4WidthSlope > -35m;

            if (earlyRunawayStillAlive && dailyMidSlope > -15m && h4MidSlope > -15m)
                score += 0.20m;

            if (earlyRunawayStillAlive && hotByVolume)
                score += 0.12m;

            if (earlyRunawayStillAlive && topPercGain)
                score += 0.14m;

            if (earlyRunawayStillAlive && mostActive)
                score += 0.08m;

            var freshLiveRunaway =
                weeklyMidLast > 15m &&
                dailyMidLast > 12m &&
                h4MidLast > 2m &&
                dailyMacdLast >= 0m &&
                h4MacdLast > -0.12m &&
                dailyMidSlope > -10m &&
                h4MidSlope > -10m &&
                dailyWidthSlope > -30m &&
                h4WidthSlope > -30m;

            if (freshLiveRunaway)
                score += 0.22m;

            if (freshLiveRunaway && topPercGain)
                score += 0.18m;

            if (freshLiveRunaway && hotByVolume)
                score += 0.15m;

            if (freshLiveRunaway && mostActive)
                score += 0.10m;

            var coolingButAlive =
                weeklyMidLast > 20m &&
                dailyMidLast > 10m &&
                h4MidLast > 0m &&
                dailyMacdLast >= 0m &&
                h4MacdLast > -0.20m &&
                dailyMidSlope > -20m &&
                h4MidSlope > -20m &&
                dailyWidthSlope > -40m &&
                h4WidthSlope > -40m;

            if (coolingButAlive && topPercGain)
                score += 0.16m;

            if (coolingButAlive && hotByVolume)
                score += 0.12m;

            var healthyConstructiveContinuation =
                dailyMidLast > 8m &&
                dailyMidLast < 55m &&
                h4MidLast > 2m &&
                h4MidLast < 45m &&
                dailyMacdLast > 0m &&
                h4MacdLast >= 0m &&
                dailyMidSlope > -10m &&
                h4MidSlope > -10m &&
                dailyMacdSlope > -0.08m &&
                h4MacdSlope > -0.08m &&
                dailyWidthSlope > -25m &&
                h4WidthSlope > -25m;

            if (healthyConstructiveContinuation)
                score += 0.26m;

            if (healthyConstructiveContinuation && topPercGain)
                score += 0.12m;

            if (healthyConstructiveContinuation && hotByVolume)
                score += 0.10m;

            if (healthyConstructiveContinuation && mostActive)
                score += 0.08m;

            var smoothContinuationAlive =
                weeklyMidLast > 15m &&
                dailyMidLast > 20m &&
                h4MidLast > 8m &&
                dailyMacdLast > 0m &&
                h4MacdLast >= 0m &&
                dailyMidSlope > -18m &&
                h4MidSlope > -18m &&
                dailyMacdSlope > -0.12m &&
                h4MacdSlope > -0.10m &&
                dailyWidthSlope > -45m &&
                h4WidthSlope > -45m;

            if (smoothContinuationAlive)
                score += 0.24m;

            if (smoothContinuationAlive && topPercGain)
                score += 0.12m;

            if (smoothContinuationAlive && hotByVolume)
                score += 0.10m;

            if (smoothContinuationAlive && mostActive)
                score += 0.08m;

            var moderateAliveNotExplosive =
                weeklyMidLast > 10m &&
                dailyMidLast > 12m &&
                dailyMidLast < 80m &&
                h4MidLast > 4m &&
                dailyMacdLast > 0m &&
                h4MacdLast > -0.05m &&
                dailyMidSlope > -14m &&
                h4MidSlope > -14m &&
                dailyWidthSlope > -35m &&
                h4WidthSlope > -35m;

            if (moderateAliveNotExplosive)
                score += 0.18m;

            if (moderateAliveNotExplosive && hotByVolume)
                score += 0.08m;

            if (moderateAliveNotExplosive && topPercGain)
                score += 0.10m;

            var staleCoolingContinuation =
                dailyMidLast > 20m &&
                h4MidLast > 0m &&
                dailyMidSlope < -12m &&
                h4MidSlope < -12m &&
                dailyMacdSlope <= -0.08m &&
                h4MacdSlope <= -0.08m;

            if (staleCoolingContinuation)
                score -= 0.45m;

            if (staleCoolingContinuation && snapshot.Current.DailyMaSignedDistancePct > 35m)
                score -= 0.20m;

            var flatWeakContinuation =
                dailyMidLast > 10m &&
                h4MidLast > 0m &&
                dailyMacdLast <= 0.08m &&
                h4MacdLast <= 0.05m &&
                dailyMidSlope <= 0m &&
                h4MidSlope <= 0m &&
                dailyWidthSlope < -10m &&
                h4WidthSlope < -10m;

            if (flatWeakContinuation)
                score -= 0.28m;

            var strongAmplitudeProxy =
                weeklyMidLast > 10m &&
                dailyMidLast > 12m &&
                h4MidLast > 4m &&
                weeklyMacdLast > -0.05m &&
                dailyMacdLast > 0.08m &&
                h4MacdLast >= 0m &&
                weeklyMidSlope > -8m &&
                dailyMidSlope > -8m &&
                h4MidSlope > -8m &&
                weeklyMacdSlope > -0.05m &&
                dailyMacdSlope > -0.05m &&
                h4MacdSlope > -0.05m &&
                weeklyWidthSlope > -15m &&
                dailyWidthSlope > -10m &&
                h4WidthSlope > -10m;

            if (strongAmplitudeProxy)
                score += 0.34m;

            if (strongAmplitudeProxy && topPercGain)
                score += 0.18m;

            if (strongAmplitudeProxy && hotByVolume)
                score += 0.14m;

            if (strongAmplitudeProxy && mostActive)
                score += 0.10m;

            var weakAmplitudeProxy =
                weeklyMidLast > 0m &&
                dailyMidLast > 8m &&
                h4MidLast >= 0m &&
                dailyMidLast < 45m &&
                weeklyMacdLast < 0.12m &&
                dailyMacdLast < 0.12m &&
                h4MacdLast < 0.08m &&
                weeklyMidSlope < 0m &&
                dailyMidSlope < -4m &&
                h4MidSlope < -4m &&
                weeklyWidthSlope < -8m &&
                dailyWidthSlope < -6m &&
                h4WidthSlope < -6m;

            if (weakAmplitudeProxy)
                score -= 0.38m;

            if (weakAmplitudeProxy && snapshot.Current.DailyMaSignedDistancePct > 20m)
                score -= 0.12m;

            var repairedHighAmplitudeLike =
                weeklyMidLast > 8m &&
                dailyMidLast > 10m &&
                h4MidLast > 4m &&
                dailyMacdLast > 0.18m &&
                h4MacdLast > 0.05m &&
                weeklyMacdLast > -0.10m &&
                dailyMidSlope > -6m &&
                h4MidSlope > -6m &&
                dailyMacdSlope > -0.04m &&
                h4MacdSlope > -0.04m &&
                dailyWidthSlope > -5m &&
                h4WidthSlope > -5m;

            if (repairedHighAmplitudeLike)
                score += 0.30m;

            if (repairedHighAmplitudeLike && hotByVolume)
                score += 0.16m;

            if (repairedHighAmplitudeLike && mostActive)
                score += 0.12m;

            if (repairedHighAmplitudeLike && topPercGain)
                score += 0.10m;

            var repairedLowAmplitudeLike =
                weeklyMidLast > 0m &&
                dailyMidLast > 8m &&
                dailyMidLast < 35m &&
                h4MidLast > 0m &&
                h4MidLast < 25m &&
                dailyMacdLast < 0.15m &&
                h4MacdLast < 0.10m &&
                weeklyMacdLast < 0.35m &&
                dailyMidSlope < 4m &&
                h4MidSlope < 4m &&
                dailyWidthSlope < 8m &&
                h4WidthSlope < 8m;

            if (repairedLowAmplitudeLike)
                score -= 0.34m;

            if (repairedLowAmplitudeLike && snapshot.Current.DailyRSI14 > 55m)
                score -= 0.10m;

            return score;
        }

        private List<CandidateDetails> ReRankCandidates(
            List<CandidateDetails> candidates,
            int finalTopCandidates,
            NextDayRankingSettings settings,
            IReadOnlyList<SeriesSimilarityTemplate> seriesSimilarityTemplates,
            SeriesTemplateFamily family)
        {
            if (candidates.Count <= 1)
                return candidates;

            var ordered = candidates
                .OrderByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
                .ThenByDescending(x => x.TradePlan.ProfitPercent)
                .ThenByDescending(x => x.Score.Score)
                .ToList();
            var rankedInputs = ordered
                .Select(x =>
                {
                    var seriesSimilarityMatch = CalculateSeriesSimilarityMatch(
                        x,
                        seriesSimilarityTemplates,
                        family,
                        settings.SeriesSimilarity);
                    var lowAmplitudeMatch = family == SeriesTemplateFamily.TodayResearchLike
                        ? CalculateSeriesSimilarityMatch(
                            x,
                            seriesSimilarityTemplates,
                            SeriesTemplateFamily.LowAmplitudeSameDay,
                            settings.SeriesSimilarity)
                        : SeriesSimilarityMatch.Empty;

                    ApplySeriesSimilarityDiagnostics(x, seriesSimilarityMatch, lowAmplitudeMatch, settings.SeriesSimilarity);

                    return new
                    {
                        Candidate = x,
                        SeriesSimilarityMatch = seriesSimilarityMatch,
                        LowAmplitudeMatch = lowAmplitudeMatch
                    };
                })
                .ToList();

            var window = family == SeriesTemplateFamily.TodayResearchLike
                ? rankedInputs.Count
                : Math.Min(
                    rankedInputs.Count,
                    Math.Max(settings.SecondPassMinimumWindow, finalTopCandidates * settings.SecondPassWindowMultiplier));

            if (window <= 1)
                return rankedInputs.Select(x => x.Candidate).ToList();

            var topWindow = rankedInputs
                .Take(window)
                .Select(x =>
                {
                    return new
                    {
                        x.Candidate,
                        AdjustedRank = (x.Candidate.Score.NextDayRank ?? decimal.MinValue) +
                            CalculateSecondPassAdjustment(
                                x.Candidate,
                                settings,
                                x.SeriesSimilarityMatch.Bonus,
                                x.LowAmplitudeMatch.Bonus)
                    };
                })
                .OrderByDescending(x => x.AdjustedRank)
                .ThenByDescending(x => x.Candidate.TradePlan.ProfitPercent)
                .ThenByDescending(x => x.Candidate.Score.Score)
                .ToList();

            for (var i = 0; i < topWindow.Count; i++)
            {
                topWindow[i].Candidate.Score.NextDayRank = decimal.Round(
                    topWindow[i].AdjustedRank,
                    4,
                    MidpointRounding.AwayFromZero);
            }

            return
            [
                .. topWindow.Select(x => x.Candidate),
                .. rankedInputs.Skip(window).Select(x => x.Candidate)
            ];
        }

        private decimal CalculateSecondPassAdjustment(
            CandidateDetails candidate,
            NextDayRankingSettings settings,
            decimal seriesSimilarityBonus,
            decimal lowAmplitudeSimilarityPenalty)
        {
            var diagnostics = candidate.Diagnostics;
            if (diagnostics == null)
                return 0m;

            var distanceTo20dHigh = candidate.Context.DistanceTo20dHigh;
            var dailyRsi14 = candidate.Context.DailyRSI14;

            var dailyMaSlope = CalculateSlope(candidate.RecentDailyMaSeries);
            var dailyRsiSlope = CalculateSlope(candidate.RecentDailyRsiSeries);
            var dailyMacdSlope = CalculateSlope(candidate.RecentDailyMacdSeries);
            var h4MaSlope = CalculateSlope(candidate.RecentH4MaSeries);
            var h4RsiSlope = CalculateSlope(candidate.RecentH4RsiSeries);
            var h4MacdSlope = CalculateSlope(candidate.RecentH4MacdSeries);
            var dailyUpMoves = CountUpMoves(candidate.RecentDailyRsiSeries);
            var h4UpMoves = CountUpMoves(candidate.RecentH4RsiSeries);

            var dailySeriesScore =
                Closeness(dailyMaSlope, settings.ResearchSeriesDailyMaSlopeTarget, settings.ResearchSeriesDailyMaSlopeTolerance) +
                Closeness(dailyRsiSlope, settings.ResearchSeriesDailyRsiSlopeTarget, settings.ResearchSeriesDailyRsiSlopeTolerance) +
                Closeness(dailyMacdSlope, settings.ResearchSeriesDailyMacdSlopeTarget, settings.ResearchSeriesDailyMacdSlopeTolerance) +
                Closeness(dailyUpMoves, settings.ResearchSeriesDailyRsiUpMovesTarget, settings.ResearchSeriesDailyRsiUpMovesTolerance);

            var h4SeriesScore =
                Closeness(h4MaSlope, settings.ResearchSeriesH4MaSlopeTarget, settings.ResearchSeriesH4MaSlopeTolerance) +
                Closeness(h4RsiSlope, settings.ResearchSeriesH4RsiSlopeTarget, settings.ResearchSeriesH4RsiSlopeTolerance) +
                Closeness(h4MacdSlope, settings.ResearchSeriesH4MacdSlopeTarget, settings.ResearchSeriesH4MacdSlopeTolerance) +
                Closeness(h4UpMoves, settings.ResearchSeriesH4RsiUpMovesTarget, settings.ResearchSeriesH4RsiUpMovesTolerance);

            var contextScore =
                Positive((-distanceTo20dHigh - 10m) / 20m) +
                Positive((58m - dailyRsi14) / 20m) +
                Positive((diagnostics.ATRRatio - 2.2m) / 2m) +
                Positive((-diagnostics.TrendPosition) / 8m) +
                Positive((-diagnostics.BBMidSignedDistancePct) / 6m);

            var seriesPenaltyScore =
                Negative(dailyMacdSlope) / 0.15m +
                Negative(h4MacdSlope) / 0.15m +
                (IsRollingOver(candidate.RecentDailyRsiSeries) ? 0.8m : 0m) +
                (IsRollingOver(candidate.RecentDailyMaSeries) ? 1.0m : 0m) +
                (IsRollingOver(candidate.RecentH4MaSeries) ? 1.2m : 0m) +
                (IsExhausted(candidate.RecentDailyRsiSeries, settings.PatternExhaustionH4RsiThreshold - 8m) ? 0.6m : 0m) +
                (IsExhausted(candidate.RecentH4RsiSeries, settings.PatternExhaustionH4RsiThreshold) ? 1.0m : 0m);

            var latePenaltyScore =
                Positive((dailyRsi14 - settings.LateContinuationDailyRsi14Threshold) / 20m) +
                Positive((distanceTo20dHigh - settings.LateContinuationDistanceTo20dHighThreshold) / 6m) +
                Positive((diagnostics.TrendPosition - settings.LateContinuationTrendPositionThreshold) / 8m) +
                Positive((diagnostics.BBMidSignedDistancePct - settings.LateContinuationBbMidThreshold) / 6m);

            var overextendedPenaltyScore =
                Positive((dailyRsi14 - settings.OverextendedDailyRsi14Threshold) / 20m) +
                Positive((diagnostics.TrendPosition - settings.OverextendedTrendPositionThreshold) / 8m) +
                Positive((diagnostics.BBMidSignedDistancePct - settings.OverextendedBbMidThreshold) / 8m);

            var bbAdjustment =
                CalculateBbSecondPassAdjustment(
                    candidate.WeeklyBbRegime,
                    candidate.WeeklyBbDirection,
                    candidate.DailyBbRegime,
                    candidate.DailyBbDirection,
                    candidate.H4BbRegime,
                    candidate.H4BbDirection);
            var freshExpansionWinnerAdjustment = CalculateFreshExpansionWinnerAdjustment(
                candidate,
                diagnostics,
                settings,
                seriesSimilarityBonus);

            return
                dailySeriesScore * settings.SecondPassDailySeriesWeight +
                h4SeriesScore * settings.SecondPassH4SeriesWeight +
                contextScore * settings.SecondPassContextWeight -
                seriesPenaltyScore * settings.SecondPassSeriesPenaltyWeight -
                latePenaltyScore * settings.SecondPassLatePenaltyWeight -
                overextendedPenaltyScore * settings.SecondPassOverextendedPenaltyWeight +
                bbAdjustment +
                CalculateLiveWinnerContinuationAdjustment(candidate, diagnostics) +
                freshExpansionWinnerAdjustment +
                seriesSimilarityBonus -
                lowAmplitudeSimilarityPenalty * settings.SeriesSimilarity.LowAmplitudePenaltyWeight;
        }

        private static decimal CalculateFreshExpansionWinnerAdjustment(
            CandidateDetails candidate,
            CandidateDiagnostics diagnostics,
            NextDayRankingSettings settings,
            decimal seriesSimilarityBonus)
        {
            diagnostics.FreshExpansionWinnerScore = null;

            if (candidate.TradePlan.ProfitPercent < settings.FreshExpansionMinProfitPct ||
                diagnostics.ATRRatio < settings.FreshExpansionMinAtrRatio)
            {
                return 0m;
            }

            var weeklyMaLast = GetLatestValue(candidate.RecentWeeklyMaSeries);
            var dailyMaLast = GetLatestValue(candidate.RecentDailyMaSeries);
            var h4MaLast = GetLatestValue(candidate.RecentH4MaSeries);
            var dailyBbWidthLast = GetLatestValue(candidate.RecentDailyBbWidthSeries);
            var h4BbWidthLast = GetLatestValue(candidate.RecentH4BbWidthSeries);
            var dailyRsiLast = GetLatestValue(candidate.RecentDailyRsiSeries);
            var h4RsiLast = GetLatestValue(candidate.RecentH4RsiSeries);
            var weeklyMacdLast = GetLatestValue(candidate.RecentWeeklyMacdSeries);
            var dailyMacdLast = GetLatestValue(candidate.RecentDailyMacdSeries);
            var h4MacdLast = GetLatestValue(candidate.RecentH4MacdSeries);

            var hasFreshWeeklyRecovery =
                weeklyMaLast >= settings.FreshExpansionMinWeeklyMa &&
                HasPreviousValueAtOrBelow(candidate.RecentWeeklyMaSeries, settings.FreshExpansionMaxPreviousWeeklyMa) &&
                HasPreviousValueAtOrBelow(candidate.RecentWeeklyMaSeries, settings.FreshExpansionMinPreviousWeeklyLow);
            var hasDailyExpansion =
                dailyMaLast >= settings.FreshExpansionMinDailyMa &&
                dailyBbWidthLast >= settings.FreshExpansionMinDailyBbWidth &&
                dailyRsiLast >= settings.FreshExpansionMinDailyRsi &&
                dailyMacdLast > settings.FreshExpansionMinMacd;
            var hasH4Expansion =
                h4MaLast >= settings.FreshExpansionMinH4Ma &&
                AverageLast(candidate.RecentH4MaSeries, 3) >= settings.FreshExpansionMinH4Ma &&
                h4BbWidthLast >= settings.FreshExpansionMinH4BbWidth &&
                h4RsiLast >= settings.FreshExpansionMinH4Rsi &&
                h4MacdLast > settings.FreshExpansionMinMacd;
            var hasWeeklyMacdRecovery =
                weeklyMacdLast >= -0.05m &&
                HasPreviousValueAtOrBelow(candidate.RecentWeeklyMacdSeries, 0m);

            if (!hasFreshWeeklyRecovery ||
                !hasDailyExpansion ||
                !hasH4Expansion ||
                !hasWeeklyMacdRecovery)
            {
                return 0m;
            }

            var score = settings.FreshExpansionWinnerBonus;

            if (seriesSimilarityBonus >= settings.SeriesSimilarity.FullMatchBonus)
                score += settings.FreshExpansionExactTemplateBonus;

            if (candidate.Score.Score >= 100m &&
                candidate.TradePlan.ProfitPercent >= 10m &&
                dailyBbWidthLast >= 60m &&
                h4BbWidthLast >= 55m)
            {
                score += settings.FreshExpansionHighConvictionBonus;
            }

            diagnostics.FreshExpansionWinnerScore = decimal.Round(score, 4, MidpointRounding.AwayFromZero);
            return score;
        }

        private static decimal CalculateLiveWinnerContinuationAdjustment(
            CandidateDetails candidate,
            CandidateDiagnostics diagnostics)
        {
            if (candidate.Scan.PresetScanCode is not ("TOP_PERC_GAIN" or "TOP_OPEN_PERC_GAIN"))
                return 0m;

            var weeklyMaDistance = GetLatestValue(candidate.RecentWeeklyMaSeries);
            var dailyMaDistance = GetLatestValue(candidate.RecentDailyMaSeries);
            var h4MaDistance = GetLatestValue(candidate.RecentH4MaSeries);

            if (weeklyMaDistance <= 0m ||
                dailyMaDistance <= 0m)
            {
                return 0m;
            }

            if (candidate.TradePlan.ProfitPercent < 3m)
                return 0m;

            if (diagnostics.ATRRatio < 4m ||
                candidate.Context.DailyRSI14 < 55m ||
                diagnostics.VolumeRatio20 < 0.05m)
            {
                return 0m;
            }

            var score = 0.90m;

            if (h4MaDistance > 0m)
                score += 0.20m;

            if (candidate.TradePlan.ProfitPercent >= 4m)
                score += 0.15m;

            return score;
        }

        private async Task<IReadOnlyList<SeriesSimilarityTemplate>> LoadSeriesSimilarityTemplatesAsync(
            SeriesSimilaritySettings settings)
        {
            if (!settings.Enabled)
                return [];

            var templates = new List<SeriesSimilarityTemplate>();
            var researchPath = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), _researchSettingsProvider.Get().OutputFile));
            var evaluationPath = Path.GetFullPath(
                Path.Combine(_pathService.GetDataRoot(), "datasets", "evaluation-dataset.csv"));

            foreach (var row in await ReadResearchRowsAsync(researchPath))
            {
                if (row.AmplitudePct < settings.MinTemplateAmplitudePct)
                    continue;

                var features = BuildTemplateFeatures(
                    row.DailyMaSeries,
                    row.DailyBbMidDistanceSeries,
                    row.DailyBbUpperDistanceSeries,
                    row.DailyBbWidthSeries,
                    row.DailyRsiSeries,
                    row.DailyMacdSeries,
                    row.WeeklyMaSeries,
                    row.WeeklyBbMidDistanceSeries,
                    row.WeeklyBbUpperDistanceSeries,
                    row.WeeklyBbWidthSeries,
                    row.WeeklyRsiSeries,
                    row.WeeklyMacdSeries,
                    row.H4MaSeries ?? [],
                    row.H4BbMidDistanceSeries ?? [],
                    row.H4BbUpperDistanceSeries ?? [],
                    row.H4BbWidthSeries ?? [],
                    row.H4RsiSeries ?? [],
                    row.H4MacdSeries ?? []);

                if (features.HasUsefulSeries)
                    templates.Add(new SeriesSimilarityTemplate(
                        row.Ticker,
                        SeriesTemplateFamily.TodayResearchLike,
                        row.AmplitudePct,
                        features));
            }

            var evaluationRows = await _evaluationDatasetCsvService.ReadAsync(evaluationPath);
            foreach (var row in evaluationRows)
            {
                if (!row.HasActiveCandidateSnapshot)
                {
                    continue;
                }

                if (settings.EnableLowAmplitudePenalty &&
                    row.CandidateSource.Equals("SameDayContinuation", StringComparison.OrdinalIgnoreCase) &&
                    row.AmplitudePct >= settings.LowAmplitudeMinTemplateAmplitudePct &&
                    row.AmplitudePct < settings.LowAmplitudeMaxTemplateAmplitudePct)
                {
                    var lowAmplitudeFeatures = BuildTemplateFeatures(
                        row.RecentDailyMaSeries,
                        row.RecentDailyBbMidDistanceSeries,
                        row.RecentDailyBbUpperDistanceSeries,
                        row.RecentDailyBbWidthSeries,
                        row.RecentDailyRsiSeries,
                        row.RecentDailyMacdSeries,
                        row.RecentWeeklyMaSeries,
                        row.RecentWeeklyBbMidDistanceSeries,
                        row.RecentWeeklyBbUpperDistanceSeries,
                        row.RecentWeeklyBbWidthSeries,
                        row.RecentWeeklyRsiSeries,
                        row.RecentWeeklyMacdSeries,
                        row.RecentH4MaSeries,
                        row.RecentH4BbMidDistanceSeries,
                        row.RecentH4BbUpperDistanceSeries,
                        row.RecentH4BbWidthSeries,
                        row.RecentH4RsiSeries,
                        row.RecentH4MacdSeries);

                    if (lowAmplitudeFeatures.HasUsefulSeries)
                    {
                        templates.Add(new SeriesSimilarityTemplate(
                            row.Ticker,
                            SeriesTemplateFamily.LowAmplitudeSameDay,
                            row.AmplitudePct,
                            lowAmplitudeFeatures));
                    }
                }

                if (row.AmplitudePct < settings.MinTemplateAmplitudePct)
                    continue;

                var family = row.CandidateSource.Equals("SameDayContinuation", StringComparison.OrdinalIgnoreCase)
                    ? SeriesTemplateFamily.TodayResearchLike
                    : SeriesTemplateFamily.Reversal;

                if (family == SeriesTemplateFamily.Reversal &&
                    !IsBelowMeanReversalTemplate(row))
                {
                    continue;
                }

                var features = BuildTemplateFeatures(
                    row.RecentDailyMaSeries,
                    row.RecentDailyBbMidDistanceSeries,
                    row.RecentDailyBbUpperDistanceSeries,
                    row.RecentDailyBbWidthSeries,
                    row.RecentDailyRsiSeries,
                    row.RecentDailyMacdSeries,
                    row.RecentWeeklyMaSeries,
                    row.RecentWeeklyBbMidDistanceSeries,
                    row.RecentWeeklyBbUpperDistanceSeries,
                    row.RecentWeeklyBbWidthSeries,
                    row.RecentWeeklyRsiSeries,
                    row.RecentWeeklyMacdSeries,
                    row.RecentH4MaSeries,
                    row.RecentH4BbMidDistanceSeries,
                    row.RecentH4BbUpperDistanceSeries,
                    row.RecentH4BbWidthSeries,
                    row.RecentH4RsiSeries,
                    row.RecentH4MacdSeries);

                if (features.HasUsefulSeries)
                    templates.Add(new SeriesSimilarityTemplate(row.Ticker, family, row.AmplitudePct, features));
            }

            var selected = templates
                .GroupBy(x => x.Family)
                .SelectMany(x => x
                    .OrderByDescending(y => y.AmplitudePct)
                    .ThenBy(y => y.Ticker, StringComparer.OrdinalIgnoreCase)
                    .Take(settings.MaxTemplatesPerFamily))
                .ToList();

            _logger.Info(
                $"Series similarity templates loaded: Total={selected.Count}, " +
                $"TodayResearchLike={selected.Count(x => x.Family == SeriesTemplateFamily.TodayResearchLike)}, " +
                $"Reversal={selected.Count(x => x.Family == SeriesTemplateFamily.Reversal)}, " +
                $"LowAmplitudeSameDay={selected.Count(x => x.Family == SeriesTemplateFamily.LowAmplitudeSameDay)}");

            return selected;
        }

        private static bool IsBelowMeanReversalTemplate(EvaluationDatasetRow row)
        {
            if (row.RecentDailyMaSeries.Count == 0 ||
                row.RecentWeeklyMaSeries.Count == 0)
            {
                return false;
            }

            return row.RecentDailyMaSeries[^1] < 0m &&
                   row.RecentWeeklyMaSeries[^1] < 0m;
        }

        private static SeriesSimilarityMatch CalculateSeriesSimilarityMatch(
            CandidateDetails candidate,
            IReadOnlyList<SeriesSimilarityTemplate> templates,
            SeriesTemplateFamily family,
            SeriesSimilaritySettings settings)
        {
            if (!settings.Enabled || templates.Count == 0)
                return SeriesSimilarityMatch.Empty;

            var candidateFeatures = BuildTemplateFeatures(
                candidate.RecentDailyMaSeries,
                candidate.RecentDailyBbMidDistanceSeries,
                candidate.RecentDailyBbUpperDistanceSeries,
                candidate.RecentDailyBbWidthSeries,
                candidate.RecentDailyRsiSeries,
                candidate.RecentDailyMacdSeries,
                candidate.RecentWeeklyMaSeries,
                candidate.RecentWeeklyBbMidDistanceSeries,
                candidate.RecentWeeklyBbUpperDistanceSeries,
                candidate.RecentWeeklyBbWidthSeries,
                candidate.RecentWeeklyRsiSeries,
                candidate.RecentWeeklyMacdSeries,
                candidate.RecentH4MaSeries,
                candidate.RecentH4BbMidDistanceSeries,
                candidate.RecentH4BbUpperDistanceSeries,
                candidate.RecentH4BbWidthSeries,
                candidate.RecentH4RsiSeries,
                candidate.RecentH4MacdSeries);

            return CalculateSeriesSimilarityMatch(candidateFeatures, templates, family, settings);
        }

        private static SeriesSimilarityMatch CalculateSeriesSimilarityMatch(
            RecentFeatureSeries recentSeries,
            IReadOnlyList<SeriesSimilarityTemplate> templates,
            SeriesTemplateFamily family,
            SeriesSimilaritySettings settings)
        {
            var candidateFeatures = BuildTemplateFeatures(
                recentSeries.DailyMaSeries,
                recentSeries.DailyBbMidDistanceSeries,
                recentSeries.DailyBbUpperDistanceSeries,
                recentSeries.DailyBbWidthSeries,
                recentSeries.DailyRsiSeries,
                recentSeries.DailyMacdSeries,
                recentSeries.WeeklyMaSeries,
                recentSeries.WeeklyBbMidDistanceSeries,
                recentSeries.WeeklyBbUpperDistanceSeries,
                recentSeries.WeeklyBbWidthSeries,
                recentSeries.WeeklyRsiSeries,
                recentSeries.WeeklyMacdSeries,
                recentSeries.H4MaSeries,
                recentSeries.H4BbMidDistanceSeries,
                recentSeries.H4BbUpperDistanceSeries,
                recentSeries.H4BbWidthSeries,
                recentSeries.H4RsiSeries,
                recentSeries.H4MacdSeries);

            return CalculateSeriesSimilarityMatch(candidateFeatures, templates, family, settings);
        }

        private static SeriesSimilarityMatch CalculateSeriesSimilarityMatch(
            SeriesFeatureSet candidateFeatures,
            IReadOnlyList<SeriesSimilarityTemplate> templates,
            SeriesTemplateFamily family,
            SeriesSimilaritySettings settings)
        {
            if (!settings.Enabled || templates.Count == 0)
                return SeriesSimilarityMatch.Empty;

            if (!candidateFeatures.HasUsefulSeries)
                return SeriesSimilarityMatch.Empty;

            var bestMatch = templates
                .Where(x => x.Family == family)
                .Select(x => new
                {
                    Template = x,
                    Distance = CalculateSeriesDistance(candidateFeatures, x.Features, settings)
                })
                .Where(x => x.Distance.HasValue)
                .OrderBy(x => x.Distance!.Value.Total)
                .FirstOrDefault();

            if (bestMatch == null || bestMatch.Distance!.Value.Total > settings.WeakMatchDistance)
                return SeriesSimilarityMatch.Empty;

            var distance = bestMatch.Distance.Value.Total;
            decimal bonus;

            if (distance <= settings.FullMatchDistance)
            {
                bonus = settings.FullMatchBonus;
            }
            else
            {
                var range = settings.WeakMatchDistance - settings.FullMatchDistance;
                if (range <= 0m)
                {
                    bonus = settings.WeakMatchBonus;
                }
                else
                {
                    var closeness = (settings.WeakMatchDistance - distance) / range;
                    bonus = settings.WeakMatchBonus +
                            closeness * (settings.FullMatchBonus - settings.WeakMatchBonus);
                }
            }

            return new SeriesSimilarityMatch(
                bestMatch.Template.Ticker,
                bestMatch.Template.Family.ToString(),
                bestMatch.Template.AmplitudePct,
                distance,
                bestMatch.Distance.Value.Daily,
                bestMatch.Distance.Value.Weekly,
                bestMatch.Distance.Value.H4,
                bonus);
        }

        private static void ApplySeriesSimilarityDiagnostics(
            CandidateDetails candidate,
            SeriesSimilarityMatch match,
            SeriesSimilarityMatch lowAmplitudeMatch,
            SeriesSimilaritySettings settings)
        {
            if (candidate.Diagnostics == null)
                return;

            candidate.Diagnostics.SeriesSimilarityTemplateTicker = match.TemplateTicker;
            candidate.Diagnostics.SeriesSimilarityTemplateFamily = match.TemplateFamily;
            candidate.Diagnostics.SeriesSimilarityTemplateAmplitudePct = match.TemplateAmplitudePct;
            candidate.Diagnostics.SeriesSimilarityDistance = match.TotalDistance;
            candidate.Diagnostics.SeriesSimilarityDailyDistance = match.DailyDistance;
            candidate.Diagnostics.SeriesSimilarityWeeklyDistance = match.WeeklyDistance;
            candidate.Diagnostics.SeriesSimilarityH4Distance = match.H4Distance;
            candidate.Diagnostics.SeriesSimilarityBonus = match.Bonus > 0m ? match.Bonus : null;
            candidate.Diagnostics.LowAmplitudeTemplateTicker = lowAmplitudeMatch.TemplateTicker;
            candidate.Diagnostics.LowAmplitudeTemplateAmplitudePct = lowAmplitudeMatch.TemplateAmplitudePct;
            candidate.Diagnostics.LowAmplitudeDistance = lowAmplitudeMatch.TotalDistance;
            candidate.Diagnostics.LowAmplitudePenalty = lowAmplitudeMatch.Bonus > 0m
                ? lowAmplitudeMatch.Bonus * settings.LowAmplitudePenaltyWeight
                : null;
        }


        private static SeriesFeatureSet BuildTemplateFeatures(
            List<decimal> dailyMa,
            List<decimal> dailyBbMid,
            List<decimal> dailyBbUpper,
            List<decimal> dailyBbWidth,
            List<decimal> dailyRsi,
            List<decimal> dailyMacd,
            List<decimal> weeklyMa,
            List<decimal> weeklyBbMid,
            List<decimal> weeklyBbUpper,
            List<decimal> weeklyBbWidth,
            List<decimal> weeklyRsi,
            List<decimal> weeklyMacd,
            List<decimal> h4Ma,
            List<decimal> h4BbMid,
            List<decimal> h4BbUpper,
            List<decimal> h4BbWidth,
            List<decimal> h4Rsi,
            List<decimal> h4Macd)
        {
            return new SeriesFeatureSet(
                Daily: [
                    dailyMa,
                    dailyBbMid,
                    dailyBbUpper,
                    dailyBbWidth,
                    dailyRsi,
                    dailyMacd
                ],
                Weekly: [
                    weeklyMa,
                    weeklyBbMid,
                    weeklyBbUpper,
                    weeklyBbWidth,
                    weeklyRsi,
                    weeklyMacd
                ],
                H4: [
                    h4Ma,
                    h4BbMid,
                    h4BbUpper,
                    h4BbWidth,
                    h4Rsi,
                    h4Macd
                ]);
        }

        private static SeriesDistance? CalculateSeriesDistance(
            SeriesFeatureSet candidate,
            SeriesFeatureSet template,
            SeriesSimilaritySettings settings)
        {
            var weightedDistance = 0m;
            var weight = 0m;
            var daily = CalculateGroupDistance(candidate.Daily, template.Daily, settings.DailyWeight, settings);
            var weekly = CalculateGroupDistance(candidate.Weekly, template.Weekly, settings.WeeklyWeight, settings);
            var h4 = CalculateGroupDistance(candidate.H4, template.H4, settings.H4Weight, settings);

            AddWeightedGroup(daily, settings.DailyWeight, ref weightedDistance, ref weight);
            AddWeightedGroup(weekly, settings.WeeklyWeight, ref weightedDistance, ref weight);
            AddWeightedGroup(h4, settings.H4Weight, ref weightedDistance, ref weight);

            return weight > 0m
                ? new SeriesDistance(weightedDistance / weight, daily, weekly, h4)
                : null;
        }

        private static void AddWeightedGroup(
            decimal? groupDistance,
            decimal groupWeight,
            ref decimal weightedDistance,
            ref decimal weight)
        {
            if (!groupDistance.HasValue)
                return;

            weightedDistance += groupDistance.Value * groupWeight;
            weight += groupWeight;
        }

        private static decimal? CalculateGroupDistance(
            IReadOnlyList<List<decimal>> candidate,
            IReadOnlyList<List<decimal>> template,
            decimal groupWeight,
            SeriesSimilaritySettings settings)
        {
            var seriesWeights = new[]
            {
                settings.MaSeriesWeight,
                settings.BbMidSeriesWeight,
                settings.BbUpperSeriesWeight,
                settings.BbWidthSeriesWeight,
                settings.RsiSeriesWeight,
                settings.MacdSeriesWeight
            };
            var pointTolerances = new[]
            {
                settings.MaPointTolerance,
                settings.BbMidPointTolerance,
                settings.BbUpperPointTolerance,
                settings.BbWidthPointTolerance,
                settings.RsiPointTolerance,
                settings.MacdPointTolerance
            };
            var weightedDistance = 0m;
            var weight = 0m;

            for (var i = 0; i < Math.Min(candidate.Count, template.Count); i++)
            {
                var distance = CalculateNormalizedPointDistance(
                    candidate[i],
                    template[i],
                    pointTolerances[i],
                    settings.RelativePointTolerance);
                if (!distance.HasValue)
                    continue;

                var currentWeight = groupWeight * seriesWeights[i];
                weightedDistance += distance.Value * currentWeight;
                weight += currentWeight;
            }

            return weight > 0m ? weightedDistance / weight : null;
        }

        private static decimal? CalculateNormalizedPointDistance(
            List<decimal> left,
            List<decimal> right,
            decimal baseTolerance,
            decimal relativeTolerance)
        {
            var count = Math.Min(left.Count, right.Count);
            if (count < 3)
                return null;

            var leftTail = left.TakeLast(count).ToArray();
            var rightTail = right.TakeLast(count).ToArray();
            var leftBase = leftTail[0];
            var rightBase = rightTail[0];
            var total = 0m;

            for (var i = 0; i < count; i++)
            {
                var leftDelta = leftTail[i] - leftBase;
                var rightDelta = rightTail[i] - rightBase;
                var diff = Math.Abs(leftDelta - rightDelta);
                var tolerance = Math.Max(baseTolerance, Math.Abs(rightDelta) * relativeTolerance);

                total += Positive(diff - tolerance);
            }

            return total / count;
        }

        private async Task<List<ResearchTopGainerDatasetRow>> ReadResearchRowsAsync(string path)
        {
            if (!File.Exists(path))
                return [];

            var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8);
            if (lines.Length <= 1)
                return [];

            var headers = SplitCsvLine(lines[0]);
            var headerIndex = headers
                .Select((name, index) => new { name, index })
                .ToDictionary(x => x.name.TrimStart('\ufeff'), x => x.index, StringComparer.OrdinalIgnoreCase);

            var properties = typeof(ResearchTopGainerDatasetRow)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.CanWrite)
                .ToArray();

            var rows = new List<ResearchTopGainerDatasetRow>();

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = SplitCsvLine(line);
                var row = new ResearchTopGainerDatasetRow { Ticker = string.Empty };

                foreach (var property in properties)
                {
                    if (!TryGetResearchColumnIndex(headerIndex, property.Name, out var index) ||
                        index >= values.Count)
                    {
                        continue;
                    }

                    property.SetValue(row, ParseResearchValue(property.PropertyType, values[index]));
                }

                if (!string.IsNullOrWhiteSpace(row.Ticker))
                    rows.Add(row);
            }

            return rows;
        }

        private static object? ParseResearchValue(Type type, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (Nullable.GetUnderlyingType(type) != null)
                    return null;

                if (type == typeof(string))
                    return string.Empty;

                if (type == typeof(List<decimal>))
                    return new List<decimal>();

                return Activator.CreateInstance(type);
            }

            var targetType = Nullable.GetUnderlyingType(type) ?? type;

            if (targetType == typeof(string))
                return raw;

            if (targetType == typeof(DateTime))
            {
                if (DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    return dt;

                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                    return dt;

                return Nullable.GetUnderlyingType(type) != null ? null : default(DateTime);
            }

            if (targetType == typeof(decimal))
            {
                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                    return dec;

                return Nullable.GetUnderlyingType(type) != null ? null : 0m;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
                    return i;

                return 0;
            }

            if (targetType == typeof(List<decimal>))
                return ParseDecimalList(raw);

            return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
        }

        private static List<decimal> ParseDecimalList(string raw)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length < 2 || trimmed == "[]")
                return [];

            if (trimmed[0] == '[' && trimmed[^1] == ']')
                trimmed = trimmed[1..^1];

            if (string.IsNullOrWhiteSpace(trimmed))
                return [];

            return
            [
                .. trimmed
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(x => decimal.TryParse(x, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec)
                        ? dec
                        : 0m)
            ];
        }

        private static bool TryGetResearchColumnIndex(
            Dictionary<string, int> headerIndex,
            string propertyName,
            out int index)
        {
            if (headerIndex.TryGetValue(propertyName, out index))
                return true;

            foreach (var alias in GetResearchColumnAliases(propertyName))
            {
                if (headerIndex.TryGetValue(alias, out index))
                    return true;
            }

            index = -1;
            return false;
        }

        private static List<string> GetResearchColumnAliases(string propertyName)
        {
            return propertyName switch
            {
                nameof(ResearchTopGainerDatasetRow.ScanTime) => ["ReferenceTime"],
                nameof(ResearchTopGainerDatasetRow.ScanPrice) => ["ReferencePrice"],
                nameof(ResearchTopGainerDatasetRow.MaxTime) => ["PeakTime"],
                nameof(ResearchTopGainerDatasetRow.MaxPrice) => ["PeakPrice"],
                nameof(ResearchTopGainerDatasetRow.AmplitudePct) => ["RunupPct"],
                nameof(ResearchTopGainerDatasetRow.PositivePotentialPct) => ["RunupPct"],
                nameof(ResearchTopGainerDatasetRow.NegativePotentialPct) => ["MaxDrawdownBeforePeakPct"],
                nameof(ResearchTopGainerDatasetRow.BarsToMax) => ["BarsToPeak"],
                nameof(ResearchTopGainerDatasetRow.DailyMaSeries) => ["DailyMaDistances"],
                nameof(ResearchTopGainerDatasetRow.DailyBbMidDistanceSeries) => ["DailyBollingerMidDistances"],
                nameof(ResearchTopGainerDatasetRow.DailyBbUpperDistanceSeries) => ["DailyBollingerUpperDistances"],
                nameof(ResearchTopGainerDatasetRow.DailyBbWidthSeries) => ["DailyBollingerBandWidths"],
                nameof(ResearchTopGainerDatasetRow.DailyRsiSeries) => ["DailyRsiValues"],
                nameof(ResearchTopGainerDatasetRow.DailyMacdSeries) => ["DailyMacdValues"],
                nameof(ResearchTopGainerDatasetRow.WeeklyMaSeries) => ["WeeklyMaDistances"],
                nameof(ResearchTopGainerDatasetRow.WeeklyBbMidDistanceSeries) => ["WeeklyBollingerMidDistances"],
                nameof(ResearchTopGainerDatasetRow.WeeklyBbUpperDistanceSeries) => ["WeeklyBollingerUpperDistances"],
                nameof(ResearchTopGainerDatasetRow.WeeklyBbWidthSeries) => ["WeeklyBollingerBandWidths"],
                nameof(ResearchTopGainerDatasetRow.WeeklyRsiSeries) => ["WeeklyRsiValues"],
                nameof(ResearchTopGainerDatasetRow.WeeklyMacdSeries) => ["WeeklyMacdValues"],
                nameof(ResearchTopGainerDatasetRow.H4MaSeries) => ["H4MaDistances"],
                nameof(ResearchTopGainerDatasetRow.H4BbMidDistanceSeries) => ["H4BollingerMidDistances"],
                nameof(ResearchTopGainerDatasetRow.H4BbUpperDistanceSeries) => ["H4BollingerUpperDistances"],
                nameof(ResearchTopGainerDatasetRow.H4BbWidthSeries) => ["H4BollingerBandWidths"],
                nameof(ResearchTopGainerDatasetRow.H4RsiSeries) => ["H4RsiValues"],
                nameof(ResearchTopGainerDatasetRow.H4MacdSeries) => ["H4MacdValues"],
                _ => []
            };
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            result.Add(sb.ToString());
            return result;
        }

        private decimal CalculatePatternSeriesAdjustment(
            RecentFeatureSeries recentSeries,
            NextDayRankingSettings settings)
        {
            decimal score = 0m;

            var constructiveDaily =
                HasPositiveSlope(recentSeries.DailyMaSeries, settings.PatternDailyMaSlopeThreshold) &&
                HasPositiveSlope(recentSeries.DailyRsiSeries, settings.PatternDailyRsiSlopeThreshold) &&
                CountUpMoves(recentSeries.DailyRsiSeries) >= 3;

            if (constructiveDaily)
                score += settings.PatternConstructiveLaunchBonus;

            var constructiveH4 =
                HasPositiveSlope(recentSeries.H4MaSeries, settings.PatternH4MaSlopeThreshold) &&
                HasPositiveSlope(recentSeries.H4RsiSeries, settings.PatternH4RsiSlopeThreshold) &&
                CountUpMoves(recentSeries.H4RsiSeries) >= 6;

            if (constructiveH4)
                score += settings.PatternH4TrendBonus;

            if (IsExhausted(recentSeries.H4RsiSeries, settings.PatternExhaustionH4RsiThreshold) ||
                IsRollingOver(recentSeries.H4MaSeries))
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
                DailyMaSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyMaSignedDistancePct),
                DailyBbMidDistanceSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyBollingerMidDistancePct),
                DailyBbUpperDistanceSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyBollingerUpperDistancePct),
                DailyBbWidthSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyBollingerBandWidthPct),
                DailyRsiSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyRSI14),
                DailyMacdSeries = BuildRecentDailySeries(candles, scanIndex, x => x.DailyMACDLineMinusSignal),
                WeeklyMaSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyMaSignedDistancePct),
                WeeklyBbMidDistanceSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyBollingerMidDistancePct),
                WeeklyBbUpperDistanceSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyBollingerUpperDistancePct),
                WeeklyBbWidthSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyBollingerBandWidthPct),
                WeeklyRsiSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyRSI14),
                WeeklyMacdSeries = BuildRecentWeeklySeries(candles, scanIndex, x => x.WeeklyMACDLineMinusSignal),
                H4MaSeries = BuildRecentH4Series(candles, scanIndex, x => x.H4MaSignedDistancePct),
                H4BbMidDistanceSeries = BuildRecentH4Series(candles, scanIndex, x => x.H4BollingerMidDistancePct),
                H4BbUpperDistanceSeries = BuildRecentH4Series(candles, scanIndex, x => x.H4BollingerUpperDistancePct),
                H4BbWidthSeries = BuildRecentH4Series(candles, scanIndex, x => x.H4BollingerBandWidthPct),
                H4RsiSeries = BuildRecentH4Series(candles, scanIndex, x => x.RSI14),
                H4MacdSeries = BuildRecentH4Series(candles, scanIndex, x => x.MACDLineMinusSignal)
            };
        }

        private BollingerStateSet BuildBollingerStateSet(RecentFeatureSeries recentSeries)
        {
            return new BollingerStateSet
            {
                Weekly = ToOutput(_bollingerFigureAnalyzer.Analyze(new BollingerFeatureSeries
                {
                    MidSeries = recentSeries.WeeklyBbMidDistanceSeries,
                    UpperDistanceSeries = recentSeries.WeeklyBbUpperDistanceSeries,
                    WidthSeries = recentSeries.WeeklyBbWidthSeries
                })),
                Daily = ToOutput(_bollingerFigureAnalyzer.Analyze(new BollingerFeatureSeries
                {
                    MidSeries = recentSeries.DailyBbMidDistanceSeries,
                    UpperDistanceSeries = recentSeries.DailyBbUpperDistanceSeries,
                    WidthSeries = recentSeries.DailyBbWidthSeries
                })),
                H4 = ToOutput(_bollingerFigureAnalyzer.Analyze(new BollingerFeatureSeries
                {
                    MidSeries = recentSeries.H4BbMidDistanceSeries,
                    UpperDistanceSeries = recentSeries.H4BbUpperDistanceSeries,
                    WidthSeries = recentSeries.H4BbWidthSeries
                }))
            };
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

        private static int CountUpMoves(List<decimal> series)
            => series.Count < 2 ? 0 : series.Zip(series.Skip(1), (a, b) => b > a ? 1 : 0).Sum();

        private static bool IsRollingOver(List<decimal> series)
            => series.Count >= 3 && series[^1] < series[^2] && series[^2] <= series[^3];

        private static bool IsExhausted(List<decimal> series, decimal threshold)
            => series.Count >= 3 &&
               series[^1] >= threshold &&
               series[^1] <= series[^2];

        private static decimal CalculateSlope(List<decimal> series)
            => series.Count >= 2
                ? decimal.Round(series[^1] - series[0], 2, MidpointRounding.AwayFromZero)
                : 0m;

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
                   HasPositiveSlope(recentSeries.H4MaSeries, settings.PatternH4MaSlopeThreshold);
        }

        private static bool IsResearchLikeReadyNow(
            RecentFeatureSeries recentSeries,
            ResearchLikeExitSettings settings)
        {
            return settings.Enabled &&
                   CalculateSlope(recentSeries.DailyRsiSeries) >= settings.MinDailyRsiSlope &&
                   CalculateSlope(recentSeries.DailyMacdSeries) >= settings.MinDailyMacdSlope &&
                   CalculateSlope(recentSeries.H4RsiSeries) >= settings.MinH4RsiSlope &&
                   CalculateSlope(recentSeries.H4MacdSeries) >= settings.MinH4MacdSlope &&
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
            RecentFeatureSeries recentSeries,
            H4BollingerEntrySettings settings,
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

            if (settings.Enabled)
            {
                adjusted = ResolveH4BbFigureEntryDiscountPct(
                    adjusted,
                    bbState,
                    recentSeries,
                    settings,
                    isBullishMinFirstSetup);
            }

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
            var dailyMidSlope = CalculateSlope(recentSeries.DailyBbMidDistanceSeries);
            var dailyMaSlope = CalculateSlope(recentSeries.DailyMaSeries);
            var dailyRsiSlope = CalculateSlope(recentSeries.DailyRsiSeries);
            var dailyMacdSlope = CalculateSlope(recentSeries.DailyMacdSeries);
            var h4MidSlope = CalculateSlope(recentSeries.H4BbMidDistanceSeries);
            var h4MaSlope = CalculateSlope(recentSeries.H4MaSeries);
            var h4RsiSlope = CalculateSlope(recentSeries.H4RsiSeries);
            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdSeries);
            var dailyMaLast = GetLatestValue(recentSeries.DailyMaSeries);
            var dailyMidLast = GetLatestValue(recentSeries.DailyBbMidDistanceSeries);
            var h4MaLast = GetLatestValue(recentSeries.H4MaSeries);
            var h4MidLast = GetLatestValue(recentSeries.H4BbMidDistanceSeries);
            var dailyRsiLast = GetLatestValue(recentSeries.DailyRsiSeries);
            var h4RsiLast = GetLatestValue(recentSeries.H4RsiSeries);
            var dailyMacdLast = GetLatestValue(recentSeries.DailyMacdSeries);
            var h4MacdLast = GetLatestValue(recentSeries.H4MacdSeries);

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
                dailyMidSlope >= settings.DailyStrongSlopeThreshold ||
                dailyRsiSlope >= settings.DailyRsiSlopeThreshold ||
                snapshot.Current.DailyRSI14 >= settings.DailyOverheatedRsiThreshold;

            var h4Weakening =
                h4MidSlope <= settings.H4WeakSlopeThreshold ||
                h4RsiSlope < 0m ||
                h4MacdSlope <= settings.H4MacdWeakDeltaThreshold ||
                bbState.H4.Direction == nameof(BollingerFigureDirection.Down) ||
                bbState.H4.Regime == nameof(BollingerFigureRegime.Collapse);

            var constructiveH4 =
                h4MidSlope > 0m &&
                h4RsiSlope > 0m &&
                h4MacdSlope > 0m &&
                bbState.H4.Direction == nameof(BollingerFigureDirection.Up) &&
                bbState.H4.Regime is nameof(BollingerFigureRegime.Runaway) or
                                     nameof(BollingerFigureRegime.Reacceleration);

            var cleanContinuation =
                dailyStrong &&
                !h4Weakening &&
                dailyMidLast > 5m &&
                h4MidLast > 0m &&
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
                 dailyMidLast >= 20m ||
                 h4MidLast >= 10m) &&
                (h4Weakening ||
                 h4MacdSlope <= 0m ||
                 IsRollingOver(recentSeries.H4RsiSeries));

            var fastContinuationShallow =
                !lateSpike &&
                (cleanContinuation ||
                 (dailyStrong && constructiveH4) ||
                 (dailyMaLast >= settings.LaunchContinuationMinDailyMaDistancePct &&
                  dailyMaLast < settings.FastContinuationMinDailyMaDistancePct &&
                  h4MaLast >= settings.LaunchContinuationMinH4MaDistancePct &&
                  dailyMaSlope >= settings.LaunchContinuationMinDailyMaSlopePct &&
                  h4RsiLast >= settings.LaunchContinuationMinH4Rsi &&
                  h4MacdLast >= settings.LaunchContinuationMinH4Macd &&
                  !h4Weakening) ||
                 (dailyMaLast >= settings.FastContinuationMinDailyMaDistancePct &&
                  h4MaLast >= settings.FastContinuationMinH4MaDistancePct &&
                  dailyRsiLast >= 55m &&
                  h4RsiLast >= 50m &&
                  dailyMacdLast >= 0m &&
                  h4MacdLast >= -0.05m &&
                  !h4Weakening));

            var moderatePullback =
                !lateSpike &&
                !fastContinuationShallow &&
                dailyMaLast >= settings.ModeratePullbackMinDailyMaDistancePct &&
                h4MaLast >= settings.ModeratePullbackMinH4MaDistancePct &&
                dailyRsiLast >= 45m &&
                h4RsiLast >= 42m &&
                h4MacdLast >= -0.35m;

            var deepPullback =
                !lateSpike &&
                !fastContinuationShallow &&
                !moderatePullback &&
                (dailyMaLast <= settings.DeepPullbackMaxMaDistancePct ||
                 h4MaLast <= settings.DeepPullbackMaxMaDistancePct) &&
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

        private static decimal? ResolveH4BbFigureEntryDiscountPct(
            decimal? currentEntryDiscountPct,
            BollingerStateSet bbState,
            RecentFeatureSeries recentSeries,
            H4BollingerEntrySettings settings,
            bool isBullishMinFirstSetup)
        {
            var latestMidDistancePctSigned = GetLatestSignedPercent(recentSeries.H4BbMidDistanceSeries);
            if (latestMidDistancePctSigned <= 0m)
                return currentEntryDiscountPct;

            var latestMidDistancePct = latestMidDistancePctSigned;
            if (latestMidDistancePct < settings.MinimumDistanceToMidPct)
                return currentEntryDiscountPct;

            var h4DirectionUp = bbState.H4.Direction == nameof(BollingerFigureDirection.Up);
            var h4DirectionDown = bbState.H4.Direction == nameof(BollingerFigureDirection.Down);
            var dailyCorrection =
                bbState.Daily.Direction == nameof(BollingerFigureDirection.Up) &&
                (bbState.Daily.Regime is nameof(BollingerFigureRegime.Pullback) or nameof(BollingerFigureRegime.Collapse));
            var h4Correction =
                (h4DirectionUp && bbState.H4.Regime == nameof(BollingerFigureRegime.Pullback)) ||
                (h4DirectionDown && bbState.H4.Regime is nameof(BollingerFigureRegime.Runaway) or nameof(BollingerFigureRegime.Collapse));

            if (!isBullishMinFirstSetup && !(dailyCorrection && h4Correction))
                return currentEntryDiscountPct;

            var midSlopePct = bbState.H4.MidSlope;
            decimal targetDiscountPct;

            if (midSlopePct >= settings.UpwardMidSlopeThresholdPct)
            {
                targetDiscountPct = latestMidDistancePct * settings.MidpointWeightWhenMidUp;

                if (bbState.H4.Direction == nameof(BollingerFigureDirection.Up) &&
                    bbState.H4.Regime == nameof(BollingerFigureRegime.Pullback))
                {
                    targetDiscountPct = decimal.Min(targetDiscountPct, 0.04m);
                }
            }
            else if (decimal.Abs(midSlopePct) <= settings.FlatMidSlopeThresholdPct)
            {
                targetDiscountPct = latestMidDistancePct * settings.MidTouchWeightWhenMidFlat + settings.BelowMidBufferPct;
            }
            else
            {
                targetDiscountPct = latestMidDistancePct * settings.MidpointWeightWhenMidDown;
            }

            targetDiscountPct = decimal.Clamp(
                targetDiscountPct,
                settings.MinimumDistanceToMidPct,
                settings.MaximumEntryDiscountPct);

            return MaxDiscount(currentEntryDiscountPct, targetDiscountPct);
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

        private static decimal GetLatestSignedPercent(List<decimal> series)
        {
            return series.Count == 0
                ? 0m
                : series[^1] / 100m;
        }

        private static decimal GetLatestValue(List<decimal> series)
        {
            return series.Count == 0
                ? 0m
                : series[^1];
        }

        private static bool HasPreviousValueAtOrBelow(List<decimal> series, decimal threshold)
        {
            return series.Count >= 2 && series.Take(series.Count - 1).Any(x => x <= threshold);
        }

        private static decimal AverageLast(List<decimal> series, int count)
        {
            if (series.Count == 0 || count <= 0)
                return 0m;

            return series
                .Skip(Math.Max(0, series.Count - count))
                .Average();
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
                $"WishList forecast input: {ticker}. " +
                $"CurrentDailyDistancePct={currentDistancePct}, " +
                $"DailyMaDelta3={snapshot.DailyMaDelta3}, " +
                $"H4MaDelta3={snapshot.H4MaDelta3}, " +
                $"DailyRsiDelta3={snapshot.DailyRsiDelta3}, " +
                $"DailyMacdDelta3={snapshot.DailyMacdDelta3}");

            if (currentDistancePct >= 0m)
            {
                _logger.Info($"WishList forecast: {ticker} reached/exceeded daily mid already.");
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
                $"WishList forecast progress: {ticker}. " +
                $"ProgressSource={progressSource}, " +
                $"ProgressPerBar={progressPerBar}");

            if (progressPerBar <= 0m)
            {
                _logger.Info($"WishList forecast failed: {ticker}. No positive progress signal.");
                return (null, null);
            }

            var remainingDistancePct = Math.Abs(currentDistancePct);
            var expectedBars = (int)Math.Ceiling((double)(remainingDistancePct / progressPerBar));

            _logger.Info(
                $"WishList forecast raw result: {ticker}. " +
                $"RemainingDistancePct={remainingDistancePct}, " +
                $"ExpectedBarsRaw={expectedBars}");

            if (expectedBars <= 0)
            {
                _logger.Info($"WishList forecast normalized to zero bars: {ticker}.");
                return (0, scanTimeMarket);
            }

            var cappedBars = Math.Min(expectedBars, 60);
            var step = EstimateMarketBarStep(candles);
            var expectedTargetMarketTime = scanTimeMarket.Add(step * cappedBars);

            _logger.Info(
                $"WishList forecast final result: {ticker}. " +
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

            if (recentSeries.H4MacdSeries.Count < 4 || candles.Count < 5)
                return false;

            var h4MacdSlope = CalculateSlope(recentSeries.H4MacdSeries);
            var h4MacdImproving =
                h4MacdSlope > 0m &&
                recentSeries.H4MacdSeries[^1] > recentSeries.H4MacdSeries[^2] &&
                recentSeries.H4MacdSeries[^2] >= recentSeries.H4MacdSeries[^3];

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

                result.Add(new Candle
                {
                    Timeframe = Timeframe.D1,
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

            if (snapshot.Current.DailyMaSignedDistancePct < 0m)
                parts.Add("below daily MA");

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
            public List<decimal> DailyMaSeries { get; init; } = [];
            public List<decimal> DailyBbMidDistanceSeries { get; init; } = [];
            public List<decimal> DailyBbUpperDistanceSeries { get; init; } = [];
            public List<decimal> DailyBbWidthSeries { get; init; } = [];
            public List<decimal> DailyRsiSeries { get; init; } = [];
            public List<decimal> DailyMacdSeries { get; init; } = [];
            public List<decimal> WeeklyMaSeries { get; init; } = [];
            public List<decimal> WeeklyBbMidDistanceSeries { get; init; } = [];
            public List<decimal> WeeklyBbUpperDistanceSeries { get; init; } = [];
            public List<decimal> WeeklyBbWidthSeries { get; init; } = [];
            public List<decimal> WeeklyRsiSeries { get; init; } = [];
            public List<decimal> WeeklyMacdSeries { get; init; } = [];
            public List<decimal> H4MaSeries { get; init; } = [];
            public List<decimal> H4BbMidDistanceSeries { get; init; } = [];
            public List<decimal> H4BbUpperDistanceSeries { get; init; } = [];
            public List<decimal> H4BbWidthSeries { get; init; } = [];
            public List<decimal> H4RsiSeries { get; init; } = [];
            public List<decimal> H4MacdSeries { get; init; } = [];
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
            Reversal,
            LowAmplitudeSameDay
        }

        private sealed record SeriesSimilarityTemplate(
            string Ticker,
            SeriesTemplateFamily Family,
            decimal AmplitudePct,
            SeriesFeatureSet Features);

        private sealed record SeriesSimilarityMatch(
            string? TemplateTicker,
            string? TemplateFamily,
            decimal? TemplateAmplitudePct,
            decimal? TotalDistance,
            decimal? DailyDistance,
            decimal? WeeklyDistance,
            decimal? H4Distance,
            decimal Bonus)
        {
            public static SeriesSimilarityMatch Empty { get; } =
                new(null, null, null, null, null, null, null, 0m);
        }

        private readonly record struct SeriesDistance(
            decimal Total,
            decimal? Daily,
            decimal? Weekly,
            decimal? H4);

        private sealed record SeriesFeatureSet(
            IReadOnlyList<List<decimal>> Daily,
            IReadOnlyList<List<decimal>> Weekly,
            IReadOnlyList<List<decimal>> H4)
        {
            public bool HasUsefulSeries =>
                Daily.Any(x => x.Count >= 3) ||
                Weekly.Any(x => x.Count >= 3) ||
                H4.Any(x => x.Count >= 3);
        }

        private sealed class WishListContext
        {
            public required StockInfo Stock { get; init; }
            public Contract? Contract { get; set; }
            public required PresetScanCode Preset { get; init; }
            public required CandidateSignalSnapshot Snapshot { get; init; }
            public required List<Candle> Candles { get; init; }
            public required DateTime ScanTimeMarket { get; init; }
            public required decimal AvgDollarVolumeDaily { get; init; }
            public required WishListItem WishListItem { get; init; }
            public TradePlanInfo? Trade { get; set; }
        }
    }
}
