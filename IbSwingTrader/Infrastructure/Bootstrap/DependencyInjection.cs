using IbSwingTrader.Analysis;
using IbSwingTrader.Commands;
using IbSwingTrader.Infrastructure.Historical;
using IbSwingTrader.Infrastructure.Logging;
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
            services.AddSingleton<ITextLogger, TextLogger>();

            services.AddSingleton<ITwsConnection, TwsConnection>();
            services.AddSingleton<IMarketDataProvider, TwsMarketDataProvider>();

            services.AddSingleton<IHistoricalRequestThrottler>(_ =>
                new HistoricalRequestThrottler(3, 250));

            services.AddSingleton<IHistoricalCache>(sp =>
            {
                var logger = sp.GetRequiredService<ITextLogger>();
                return new HistoricalCache("cache", logger);
            });

            services.AddSingleton<IHistoricalRetryPolicy, HistoricalRetryPolicy>();
            services.AddSingleton<IHistoricalDataService, HistoricalDataService>();

            services.AddSingleton<IFeatureEngine, FeatureEngine>();
            services.AddSingleton<ICandidateScore, CandidateScore>();
            services.AddSingleton<IContractResolver, TwsContractResolver>();

            services.AddSingleton<ICsvWriter, CsvDatasetWriter>();
            services.AddSingleton<IFutureStatsCalculator, FutureStatsCalculator>();

            services.AddSingleton<ITradeDatasetBuilder>(sp =>
                new TradeDatasetBuilder(
                    sp.GetRequiredService<IFeatureEngine>(),
                    sp.GetRequiredService<ICandidateScore>(),
                    sp.GetRequiredService<IFutureStatsCalculator>(),
                    sp.GetRequiredService<ITextLogger>()));

            services.AddSingleton<IStockUniverseProvider, TwsStockUniverseProvider>();
            services.AddSingleton<IStockPreFilter, StockPreFilter>();
            services.AddSingleton<ICandidateFilter, CandidateFilter>();
            services.AddSingleton<ITradeBuilder, TradeBuilder>();
            services.AddSingleton<IScanCodeInfoService, ScanCodeInfoService>();
            services.AddSingleton<ICandidateFinder, CandidateFinder>();
            services.AddSingleton<ICandidateResultWriter, CandidateResultWriter>();

            services.AddSingleton<IAmbiguousBarResolver>(sp =>
                new AmbiguousBarResolver(
                    sp.GetRequiredService<IHistoricalDataService>(),
                    sp.GetRequiredService<ITextLogger>()));

            services.AddSingleton<ICandidateEvaluator, CandidateEvaluator>();
            services.AddSingleton<IJsonFileService, JsonFileService>();
            services.AddSingleton<ICandidateEvaluationCsvService, CandidateEvaluationCsvService>();
            services.AddSingleton<IProcessedCandidateFilesService, ProcessedCandidateFilesService>();
            services.AddSingleton<IFileHashService, FileHashService>();

            services.AddTransient<BuildDatasetCommand>();
            services.AddTransient<GetCandidatesCommand>();

            services.AddTransient<EvaluateCandidatesFolderCommand>(sp =>
                new EvaluateCandidatesFolderCommand(
                    sp.GetRequiredService<ITwsConnection>(),
                    sp.GetRequiredService<ICandidateEvaluator>(),
                    sp.GetRequiredService<IJsonFileService>(),
                    sp.GetRequiredService<ICandidateEvaluationCsvService>(),
                    sp.GetRequiredService<IProcessedCandidateFilesService>(),
                    sp.GetRequiredService<IFileHashService>(),
                    sp.GetRequiredService<ITextLogger>(),
                    candidatesFolder: "candidates",
                    evaluationsFolder: "evaluations",
                    manifestPath: "manifests/processed-candidate-files.json"));

            services.AddTransient<GetScannerParamsCommand>();
            services.AddTransient<DownloadFundamentalSnapshotCommand>();

            return services;
        }
    }
}
