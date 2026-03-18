using System.Collections.Concurrent;
using IBApi;
using IbSwingTrader.Analysis;
using IbSwingTrader.Commands;
using IbSwingTrader.Infrastructure.Bootstrap;
using IbSwingTrader.Infrastructure.Historical;
using IbSwingTrader.Infrastructure.Logging;
using IbSwingTrader.MarketData.Csv;
using IbSwingTrader.MarketData.IB;
using IbSwingTrader.Models;
using IbSwingTrader.Services;
using IbSwingTrader.Services.CandidateFiltering;

Dictionary<string, string> TickerAliases = new(StringComparer.OrdinalIgnoreCase)
{
    ["NYCB"] = "FLG"
};

var services = ConfigureServices();

if (args.Length == 0)
{
    services.Logger.Info("Usage:");
    services.Logger.Info("  build-dataset <trades.csv> <dataset.csv>");
    services.Logger.Info("  get-candidates");
    return;
}

var command = args[0];

switch (command)
{
    case "build-dataset":
        await RunBuildDataset(args, services);
        break;

    case "get-candidates":
        await RunGetCandidates();
        break;

    case "get-scanner-params":
        await services.GetScannerParamsCommand.RunAsync();
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
    var cache = new HistoricalCache("cache");
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
        Connection = connection,
        DatasetBuilder = new TradeDatasetBuilder(featureEngine, candidateScore, new FutureStatsCalculator(), logger),
        CsvWriter = new CsvDatasetWriter(),
        ContractResolver = contractResolver,
        HistoricalService = historicalService,
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
                new ScannerPresetService(),
                logger),
                new CandidateResultWriter()),
        GetScannerParamsCommand = new GetScannerParamsCommand(connection)
    };
}

async Task RunBuildDataset(string[] args, Services services)
{
    if (args.Length < 3)
    {
        services.Logger.Info("Usage: build-dataset <trades.csv> <dataset.csv>");
        return;
    }

    var tradesPath = args[1];
    var datasetPath = args[2];

    var trades = CsvTradeReader.Read(tradesPath);

    if (trades.Count == 0)
    {
        services.Logger.Error("No trades found");
        return;
    }

    var grouped = trades
        .GroupBy(t => t.Ticker)
        .OrderBy(g => g.Key)
        .ToList();

    services.Logger.Info($"Tickers found: {grouped.Count}");

    services.Connection.Connect();
    await services.Connection.Ready.Task;

    var semaphore = new SemaphoreSlim(3);
    var failedRequests = new ConcurrentBag<FailedHistoryRequest>();

    var tasks = new List<Task<List<TradeDatasetRow>>>();

    foreach (var g in grouped)
        tasks.Add(ProcessTicker(g));

    var results = await Task.WhenAll(tasks);

    var allRows = results.SelectMany(r => r).ToList();

    services.Logger.EmptyLine();
    services.Logger.Info($"Total dataset rows: {allRows.Count}");

    services.CsvWriter.Write(datasetPath, allRows);

    services.Logger.Info($"Dataset saved: {datasetPath}");

    services.Logger.EmptyLine();
    services.Logger.Info($"Failed requests: {failedRequests.Count}");

    services.Logger.EmptyLine();
    var failedTable = FailedHistoryRequestTableFormatter.Format(failedRequests);
    services.Logger.InfoBlock("FAILED HISTORY REQUESTS", failedTable);

    async Task<List<TradeDatasetRow>> ProcessTicker(IGrouping<string, TradeRecord> g)
    {
        await semaphore.WaitAsync();

        try
        {
            var originalTicker = g.Key.Trim().ToUpperInvariant();
            var requestTicker = originalTicker;

            if (TickerAliases.TryGetValue(originalTicker, out var mapped))
            {
                services.Logger.Info($"Ticker remapped: {originalTicker} → {mapped}");
                requestTicker = mapped;
            }

            var tickerTrades = g.ToList();

            var earliest = tickerTrades.Min(t => t.EntryTimeUtc);
            var latest = tickerTrades.Max(t => t.ExitTimeUtc);

            // Слева запас под warmup / индикаторы
            var start = earliest.AddDays(-120);

            // Справа запас под future bars / target
            var end = latest.AddDays(21);

            services.Logger.EmptyLine();
            services.Logger.Info($"Ticker: {originalTicker}");
            services.Logger.Info($"Trades: {tickerTrades.Count}");
            services.Logger.Info(
                $"Range: {start:yyyy-MM-dd} -> {end:yyyy-MM-dd} " +
                $"(last trade exit: {latest:yyyy-MM-dd})");

            Contract contract;

            try
            {
                contract = await services.ContractResolver.ResolveStockAsync(requestTicker);

                services.Logger.Info(
                    $"Resolved contract: {contract.Symbol} " +
                    $"conId={contract.ConId} " +
                    $"exchange={contract.Exchange}");
            }
            catch (Exception ex)
            {
                var problem = $"Failed to resolve contract: {ex.Message}";
                services.Logger.Error($"{originalTicker}: {problem}");

                failedRequests.Add(new FailedHistoryRequest
                {
                    Ticker = originalTicker,
                    Problem = problem
                });

                return [];
            }

            List<Candle>? candles;

            try
            {
                candles = await services.HistoricalService.GetCandlesRange(
                    requestTicker,
                    contract,
                    Timeframe.H4,
                    start,
                    end);
            }
            catch (Exception ex)
            {
                var problem = $"Failed to load candles: {ex.Message}";
                services.Logger.Error($"{originalTicker}: {problem}");

                failedRequests.Add(new FailedHistoryRequest
                {
                    Ticker = originalTicker,
                    Problem = problem
                });

                return [];
            }

            if (candles == null || candles.Count == 0)
            {
                var problem = "No market data";
                services.Logger.Info($"Skipping {originalTicker} — {problem}");

                failedRequests.Add(new FailedHistoryRequest
                {
                    Ticker = originalTicker,
                    Problem = problem
                });

                return [];
            }

            if (candles.Count < 60)
            {
                var problem = $"Not enough candles: {candles.Count}";
                services.Logger.Info($"Skipping {originalTicker} — {problem}");

                failedRequests.Add(new FailedHistoryRequest
                {
                    Ticker = originalTicker,
                    Problem = problem
                });

                return [];
            }

            List<TradeDatasetRow> rows;

            try
            {
                rows = services.DatasetBuilder.Build(tickerTrades, candles);

                if (rows.Count == 0)
                {
                    var problem = "No dataset rows built";

                    services.Logger.Info($"Skipping {originalTicker} — {problem}");

                    failedRequests.Add(new FailedHistoryRequest
                    {
                        Ticker = originalTicker,
                        Problem = problem
                    });

                    return [];
                }
            }
            catch (Exception ex)
            {
                var problem = $"Dataset build failed: {ex.Message}";
                services.Logger.Error($"{originalTicker}: {problem}");

                failedRequests.Add(new FailedHistoryRequest
                {
                    Ticker = originalTicker,
                    Problem = problem
                });

                return [];
            }

            services.Logger.Info($"Rows built for {originalTicker}: {rows.Count}");

            return rows;
        }
        finally
        {
            semaphore.Release();
        }
    }
}

async Task RunGetCandidates()
{
    await services.GetCandidatesCommand.RunAsync();
}