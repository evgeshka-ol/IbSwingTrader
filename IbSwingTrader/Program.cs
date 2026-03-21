using IbSwingTrader.Analysis;
using IbSwingTrader.Commands;
using IbSwingTrader.Infrastructure.Bootstrap;
using IbSwingTrader.Infrastructure.Historical;
using IbSwingTrader.Infrastructure.Logging;
using IbSwingTrader.MarketData.Csv;
using IbSwingTrader.MarketData.IB;
using IbSwingTrader.Services;
using IbSwingTrader.Services.CandidateEvaluation;
using IbSwingTrader.Services.CandidateFiltering;

var services = ConfigureServices();

if (args.Length == 0)
{
    services.Logger.Info("Usage:");
    services.Logger.Info("  build-dataset <trades.csv> <dataset.csv>");
    services.Logger.Info("  get-candidates");
    services.Logger.Info("  evaluate-candidates");
    services.Logger.Info("  get-scanner-params");
    services.Logger.Info("  download-fundamental-snapshot");
    return;
}

var command = args[0];

switch (command)
{
    case "build-dataset":
        if (args.Length < 3)
        {
            services.Logger.Info("Usage: build-dataset <trades.csv> <dataset.csv>");
            return;
        }
        await services.BuildDatasetCommand.RunAsync(args[1], args[2]);
        break;

    case "get-candidates":
        await services.GetCandidatesCommand.RunAsync();
        break;

    case "evaluate-candidates":
        await services.EvaluateCandidatesFolderCommand.RunAsync();
        break;

    case "get-scanner-params":
        await services.GetScannerParamsCommand.RunAsync();
        break;

    case "download-fundamental-snapshot":
        await services.DownloadFundamentalSnapshotCommand.RunAsync();
        break;

    default:
        services.Logger.Error("Unknown command");
        break;
}

static Services ConfigureServices()
{
    var logger = new TextLogger();
    var connection = new TwsConnection(logger);

    var provider = new TwsMarketDataProvider(connection, logger);

    var throttler = new HistoricalRequestThrottler(3, 250);
    var cache = new HistoricalCache("cache", logger);
    var retryPolicy = new HistoricalRetryPolicy();

    var historicalService = new HistoricalDataService(
        provider,
        throttler,
        cache,
        retryPolicy,
        logger);

    var featureEngine = new FeatureEngine();
    var candidateScore = new CandidateScore();
    var contractResolver = new TwsContractResolver(connection, logger);
    return new Services
    {
        Logger = logger,
        BuildDatasetCommand = new BuildDatasetCommand(connection,
            new CsvDatasetWriter(),
            contractResolver,
            historicalService,
            new TradeDatasetBuilder(featureEngine, candidateScore, new FutureStatsCalculator(), logger),
            logger),
        GetCandidatesCommand = new GetCandidatesCommand(
            new CandidateFinder(
                new TwsStockUniverseProvider(connection),
                new StockPreFilter(logger),
                contractResolver,
                provider,
                featureEngine,
                new CandidateFilter(logger),
                candidateScore,
                new TradeBuilder(),
                new ScanCodeInfoService(),
                logger),
                new CandidateResultWriter()),
        EvaluateCandidatesFolderCommand = new EvaluateCandidatesFolderCommand(
            connection,
            new CandidateEvaluator(contractResolver, historicalService, new AmbiguousBarResolver(historicalService, logger), logger),
            new JsonFileService(),
            new CandidateEvaluationCsvService(),
            new ProcessedCandidateFilesService(),
            new FileHashService(),
            logger,
            candidatesFolder: "candidates",
            evaluationsFolder: "evaluations",
            manifestPath: "manifests/processed-candidate-files.json"),
        GetScannerParamsCommand = new GetScannerParamsCommand(connection),
        DownloadFundamentalSnapshotCommand = new DownloadFundamentalSnapshotCommand(
            connection,
            contractResolver,
            logger)
    };
}
