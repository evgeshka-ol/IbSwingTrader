using Microsoft.Extensions.DependencyInjection;

namespace IbSwingTrader.App.Bootstrap
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddIbSwingTrader(
            this IServiceCollection services)
        {
            // core settings
            services.AddSingleton<IAgentSettingsProvider>(
                _ => new AgentSettingsProvider("agentsettings.json"));

            services.AddSingleton<IAgentPathService, AgentPathService>();

            // section settings providers
            services.AddSingleton<ITwsSettingsProvider, TwsSettingsProvider>();
            services.AddSingleton<IMarketSettingsProvider, MarketSettingsProvider>();
            services.AddSingleton<IBuildDatasetSettingsProvider, BuildDatasetSettingsProvider>();
            services.AddSingleton<IFeatureCalculationSettingsProvider, FeatureCalculationSettingsProvider>();
            services.AddSingleton<IGetCandidatesSettingsProvider, GetCandidatesSettingsProvider>();
            services.AddSingleton<ICandidateEvaluationSettingsProvider, CandidateEvaluationSettingsProvider>();
            services.AddSingleton<IWishListEvaluationSettingsProvider, WishListEvaluationSettingsProvider>();
            services.AddSingleton<ICsvTradeReaderSettingsProvider, CsvTradeReaderSettingsProvider>();
            services.AddSingleton<IMarketSessionSettingsProvider, MarketSessionSettingsProvider>();
            services.AddSingleton<ILoggingSettingsProvider, LoggingSettingsProvider>();

            // shared infrastructure
            services.AddSingleton<IObjectPropertyReader, ObjectPropertyReader>();
            services.AddSingleton<ICompositePropertyJsonBuilder, CompositePropertyJsonBuilder>();
            services.AddSingleton<INumberTextFormatter, NumberTextFormatter>();
            services.AddSingleton<IArrayCellFormatter, ArrayCellFormatter>();
            services.AddSingleton<IConsoleColorWriter, ConsoleColorWriter>();
            services.AddSingleton<ITextLogger, TextLogger>();
            services.AddSingleton<ICandidateResultWriter, CandidateResultWriter>();
            services.AddSingleton<IWishListResultWriter, WishListResultWriter>();

            // tws / market data
            services.AddSingleton<ITwsConnection, TwsConnection>();
            services.AddSingleton<IMarketDataProvider, TwsMarketDataProvider>();
            services.AddSingleton<IContractResolver, TwsContractResolver>();
            services.AddSingleton<IStockUniverseProvider, TwsStockUniverseProvider>();

            // market sessions
            services.AddSingleton<ILocalMarketScheduleProvider, LocalMarketScheduleProvider>();
            services.AddSingleton<IMarketScheduleCache, InMemoryMarketScheduleCache>();
            services.AddSingleton<ITwsMarketScheduleProvider, TwsMarketScheduleProvider>();
            services.AddSingleton<IMarketScheduleResolver, MarketScheduleResolver>();
            services.AddSingleton<IMarketGapAnalyzer, MarketGapAnalyzer>();
            services.AddSingleton<IMarketCoverageService, MarketCoverageService>();

            // historical
            services.AddSingleton<IFailedHistoryRequestTableFormatter, FailedHistoryRequestTableFormatter>();
            services.AddSingleton<IHistoricalRequestThrottler, HistoricalRequestThrottler>();
            services.AddSingleton<IHistoricalCache, HistoricalCache>();
            services.AddSingleton<IHistoricalRetryPolicy, HistoricalRetryPolicy>();
            services.AddSingleton<IHistoricalDataService, HistoricalDataService>();
            services.AddSingleton<IAmbiguousBarResolver, AmbiguousBarResolver>();

            // dataset / analysis
            services.AddSingleton<ICsvTradeReader, CsvTradeReader>();
            services.AddSingleton<IFeatureEngine, FeatureEngine>();
            services.AddSingleton<ICsvWriter, CsvDatasetWriter>();
            services.AddSingleton<ITradeDatasetBuilder, TradeDatasetBuilder>();

            // wish list
            services.AddSingleton<IWishListFilter, WishListFilter>();
            services.AddSingleton<IWishListScore, WishListScore>();
            services.AddSingleton<IWishListReader, WishListReader>();
            services.AddSingleton<IWishListMerger, WishListMerger>();

            // candidate search
            services.AddSingleton<ICandidateSignalAnalyzer, CandidateSignalAnalyzer>();
            services.AddSingleton<ICandidateScore, CandidateScore>();
            services.AddSingleton<IStockPreFilter, StockPreFilter>();
            services.AddSingleton<ICandidateFilter, CandidateFilter>();
            services.AddSingleton<ITradeBuilder, TradeBuilder>();
            services.AddSingleton<IScanCodeInfoService, ScanCodeInfoService>();
            services.AddSingleton<ICandidateFinder, CandidateFinder>();

            // candidate evaluation
            services.AddSingleton<ICandidateEvaluator, CandidateEvaluator>();
            services.AddSingleton<IWishListEvaluator, WishListEvaluator>();
            services.AddSingleton<IJsonFileService, JsonFileService>();
            services.AddSingleton<ICandidateEvaluationCsvService, CandidateEvaluationCsvService>();
            services.AddSingleton<IProcessedCandidateFilesService, ProcessedCandidateFilesService>();
            services.AddSingleton<IFileHashService, FileHashService>();

            // commands
            services.AddTransient<BuildDatasetCommand>();
            services.AddTransient<GetCandidatesCommand>();
            services.AddTransient<EvaluateTickersCommand>();
            services.AddTransient<GetScannerParamsCommand>();
            services.AddTransient<DownloadFundamentalSnapshotCommand>();

            return services;
        }
    }
}
