using IbSwingTrader.Analysis;
using IbSwingTrader.Commands;
using IbSwingTrader.Infrastructure.Historical;
using IbSwingTrader.Infrastructure.Logging;
using IbSwingTrader.Infrastructure.Settings;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Interfaces.IbSwingTrader.Interfaces;
using IbSwingTrader.MarketData.Csv;
using IbSwingTrader.MarketData.IB;
using IbSwingTrader.Services;
using IbSwingTrader.Services.CandidateEvaluation;
using IbSwingTrader.Services.CandidateFiltering;
using Microsoft.Extensions.DependencyInjection;

namespace IbSwingTrader.Infrastructure.Bootstrap
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
            services.AddSingleton<IEvaluationSettingsProvider, EvaluationSettingsProvider>();
            services.AddSingleton<ICsvTradeReaderSettingsProvider, CsvTradeReaderSettingsProvider>();

            // shared infrastructure
            services.AddSingleton<INumberTextFormatter, NumberTextFormatter>();
            services.AddSingleton<IConsoleColorWriter, ConsoleColorWriter>();
            services.AddSingleton<IObjectPropertyReader, ObjectPropertyReader>();
            services.AddSingleton<ITextLogger, TextLogger>();

            // tws / market data
            services.AddSingleton<ITwsConnection, TwsConnection>();
            services.AddSingleton<IMarketDataProvider, TwsMarketDataProvider>();
            services.AddSingleton<IContractResolver, TwsContractResolver>();
            services.AddSingleton<IStockUniverseProvider, TwsStockUniverseProvider>();

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
            services.AddSingleton<ICandidateScore, CandidateScore>();
            services.AddSingleton<ICsvWriter, CsvDatasetWriter>();
            services.AddSingleton<IFutureStatsCalculator, FutureStatsCalculator>();
            services.AddSingleton<ITradeDatasetBuilder, TradeDatasetBuilder>();

            // candidate search
            services.AddSingleton<IStockPreFilter, StockPreFilter>();
            services.AddSingleton<ICandidateFilter, CandidateFilter>();
            services.AddSingleton<ITradeBuilder, TradeBuilder>();
            services.AddSingleton<IScanCodeInfoService, ScanCodeInfoService>();
            services.AddSingleton<ICandidateFinder, CandidateFinder>();
            services.AddSingleton<ICandidateResultWriter, CandidateResultWriter>();

            // candidate evaluation
            services.AddSingleton<ICandidateEvaluator, CandidateEvaluator>();
            services.AddSingleton<IJsonFileService, JsonFileService>();
            services.AddSingleton<ICandidateEvaluationCsvService, CandidateEvaluationCsvService>();
            services.AddSingleton<IProcessedCandidateFilesService, ProcessedCandidateFilesService>();
            services.AddSingleton<IFileHashService, FileHashService>();

            // commands
            services.AddTransient<BuildDatasetCommand>();
            services.AddTransient<GetCandidatesCommand>();
            services.AddTransient<EvaluateCandidatesFolderCommand>();
            services.AddTransient<GetScannerParamsCommand>();
            services.AddTransient<DownloadFundamentalSnapshotCommand>();

            return services;
        }
    }
}