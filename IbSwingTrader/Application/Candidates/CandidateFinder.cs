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
        private readonly INumberTextFormatter _fmt = fmt;
        private readonly ITextLogger _logger = logger;
        private readonly NextDayRankingSettings _nextDayRankingSettings = getCandidatesSettingsProvider.Get().NextDayRanking;
        private const int RecentDailySeriesLength = 6;
        private const int RecentWeeklySeriesLength = 5;
        private const int RecentH4SeriesLength = 12;

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
            var processedScannedTickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var preset in _scannerPresets.GetAll())
            {
                var stocks = await _stockUniverseProvider.GetStocksAsync(preset.ScanCode);

                foreach (var stock in stocks)
                {
                    if (!_preFilter.Pass(stock))
                        continue;

                    if (!processedScannedTickers.Add(stock.Ticker))
                    {
                        _logger.Info($"Skipping duplicate scanned ticker: {stock.Ticker} ({preset.ScanCode})");
                        continue;
                    }

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

                    if (!_wishListFilter.Pass(snapshot, lastPrice, avgDollarVolume))
                    {
                        if (!ShouldBypassWishListFilterForLiveScan(snapshot, diagnostics, entryScore))
                        {
                            _logger.Info($"Wish list rejected: {stock.Ticker}");
                            continue;
                        }

                        _logger.Info(
                            $"Wish list live-scan bypass applied: {stock.Ticker}. " +
                            $"EntryScore={_fmt.Generic(entryScore)}, " +
                            $"DailyRsi14={_fmt.Generic(snapshot.Current.DailyRSI14)}, " +
                            $"DailyDistance={_fmt.Generic(snapshot.Current.DailyMaSignedDistancePct)}%, " +
                            $"AtrRatio={_fmt.Generic(diagnostics.ATRRatio)}, " +
                            $"VolumeRatio20={_fmt.Generic(diagnostics.VolumeRatio20)}");
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

                await TryAddCandidate(
                    candidateResults,
                    mergedWishItem,
                    ctx,
                    isFromWishlist: true,
                    marketTimezone,
                    bucketName: "candidates",
                    rejectionLogPrefix: "Entry rejected after wish list pass");
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
                        candidateResults,
                        mergedWishItem,
                        ctx,
                        isFromWishlist: false,
                        marketTimezone,
                        bucketName: "fallback candidates",
                        rejectionLogPrefix: "Entry rejected after same-day fallback");
                }

                _logger.Info($"Same-day market-scan fallback completed. Candidates={candidateResults.Count}");
            }

            var promotedTickers = candidateResults.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sameDayCandidates = await BuildPremarketSummaryCandidates(
                mergedWishList,
                mergedMap,
                scannedWishListContexts,
                promotedTickers,
                marketNow,
                marketTimezone);

            var finalWishList = mergedWishList
                .Where(x => !promotedTickers.Contains(x.Ticker))
                .ToList();

            var finalForecastedCount = finalWishList.Count(x => x.ExpectedBarsToTarget != null);
            _logger.Info($"WishList final. Total={finalWishList.Count}, WithForecast={finalForecastedCount}");

            var finalCandidates = ReRankCandidates(
                candidateResults.Values.ToList(),
                getCandidatesSettings.FinalTopCandidates,
                _nextDayRankingSettings);

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

                var diagnostics = BuildDiagnostics(ctx.Snapshot, ctx.Candles);
                var entryScore = _candidateScore.Calculate(ctx.Snapshot);

                if (entryScore < settings.MinEntryScore)
                    continue;

                if (!ShouldBypassWishListFilterForLiveScan(ctx.Snapshot, diagnostics, entryScore))
                    continue;

                var trade = ctx.Trade ??= await BuildTradePlan(ctx);
                if (trade.ProfitPercent < settings.MinPlannedProfitPct)
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
                if (item.WishListItem.Score.Score > existing.WishListItem.Score.Score)
                {
                    results[item.Stock.Ticker] = item;

                    _logger.Info(
                        $"Ticker {item.Stock.Ticker} replaced existing wish list item with higher score. " +
                        $"Old preset: {existing.Preset.ScanCode}, new preset: {item.Preset.ScanCode}");
                }
                else
                {
                    _logger.Info(
                        $"Ticker {item.Stock.Ticker} already exists in wish list. " +
                        $"Keeping existing item from preset {existing.Preset.ScanCode}");
                }
            }
            else
            {
                results[item.Stock.Ticker] = item;
                _logger.Info($"Ticker {item.Stock.Ticker} added to wish list. Preset: {item.Preset.ScanCode}");
            }
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
            string rejectionLogPrefix)
        {
            var diagnostics = BuildDiagnostics(ctx.Snapshot, ctx.Candles);
            var needsDeeperEntry = ResolveNeedsDeeperEntry(ctx.Snapshot, diagnostics);
            var entryScore = _candidateScore.Calculate(ctx.Snapshot);
            var candidateFilterSettings = _getCandidatesSettingsProvider.Get().CandidateFilter;

            if (!_candidateFilter.Pass(ctx.Snapshot, 0m, ctx.AvgDollarVolumeDaily))
            {
                _logger.Info($"{rejectionLogPrefix}: {ctx.Stock.Ticker}");
                return;
            }

            var trade = ctx.Trade ??= await BuildTradePlan(ctx);

            if (trade.ProfitPercent < candidateFilterSettings.MinPlannedProfitPct)
            {
                _logger.Info(
                    $"{rejectionLogPrefix}: {ctx.Stock.Ticker}. " +
                    $"Planned profit is too small: ProfitPercent={_fmt.Percent(trade.ProfitPercent)}%, " +
                    $"MinRequired={_fmt.Percent(candidateFilterSettings.MinPlannedProfitPct)}%");
                return;
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

            var targetForecast = CalculateWishListTargetForecast(
                stock.Ticker,
                snapshot,
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
                recentSeries);

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
            RecentFeatureSeries recentSeries)
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

            return decimal.Round(score, 4, MidpointRounding.AwayFromZero);
        }

        private List<CandidateDetails> ReRankCandidates(
            List<CandidateDetails> candidates,
            int finalTopCandidates,
            NextDayRankingSettings settings)
        {
            if (candidates.Count <= 1)
                return candidates;

            var ordered = candidates
                .OrderByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
                .ThenByDescending(x => x.TradePlan.ProfitPercent)
                .ThenByDescending(x => x.Score.Score)
                .ToList();

            var window = Math.Min(
                ordered.Count,
                Math.Max(settings.SecondPassMinimumWindow, finalTopCandidates * settings.SecondPassWindowMultiplier));

            if (window <= 1)
                return ordered;

            var topWindow = ordered
                .Take(window)
                .Select(x => new
                {
                    Candidate = x,
                    AdjustedRank = (x.Score.NextDayRank ?? decimal.MinValue) + CalculateSecondPassAdjustment(x, settings)
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
                .. ordered.Skip(window)
            ];
        }

        private decimal CalculateSecondPassAdjustment(
            CandidateDetails candidate,
            NextDayRankingSettings settings)
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

            return
                dailySeriesScore * settings.SecondPassDailySeriesWeight +
                h4SeriesScore * settings.SecondPassH4SeriesWeight +
                contextScore * settings.SecondPassContextWeight -
                seriesPenaltyScore * settings.SecondPassSeriesPenaltyWeight -
                latePenaltyScore * settings.SecondPassLatePenaltyWeight -
                overextendedPenaltyScore * settings.SecondPassOverextendedPenaltyWeight;
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
