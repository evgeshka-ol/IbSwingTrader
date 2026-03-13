using IBApi;
using IbSwingTrader.Analysis;
using IbSwingTrader.Infrastructure.Bootstrap;
using IbSwingTrader.Infrastructure.Historical;
using IbSwingTrader.Infrastructure.Logging;
using IbSwingTrader.MarketData.Csv;
using IbSwingTrader.MarketData.IB;
using IbSwingTrader.Models;
using IbSwingTrader.Services;

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

    default:
        services.Logger.Error("Unknown command");
        break;
}

static Services ConfigureServices()
{
    var logger = new SimpleLogger();
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

    return new Services
    {
        Logger = logger,
        Connection = connection,
        DatasetBuilder = new TradeDatasetBuilder(),
        CsvWriter = new CsvDatasetWriter(),
        ContractResolver = new TwsContractResolver(connection, logger),
        HistoricalService = historicalService
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

    var tasks = new List<Task<List<TradeDatasetRow>>>();

    foreach (var g in grouped)
        tasks.Add(ProcessTicker(g));

    var results = await Task.WhenAll(tasks);

    var allRows = results.SelectMany(r => r).ToList();

    services.Logger.EmptyLine();
    services.Logger.Info($"Total dataset rows: {allRows.Count}");

    services.CsvWriter.Write(datasetPath, allRows);

    services.Logger.Info($"Dataset saved: {datasetPath}");

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

            var start = earliest.AddDays(-60);

            services.Logger.EmptyLine();
            services.Logger.Info($"Ticker: {originalTicker}");
            services.Logger.Info($"Trades: {tickerTrades.Count}");
            services.Logger.Info($"Range: {start:yyyy-MM-dd} -> {latest:yyyy-MM-dd}");

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
                services.Logger.Error($"Failed to resolve contract for {requestTicker}: {ex.Message}");
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
                    latest);
            }
            catch (Exception ex)
            {
                services.Logger.Error($"Failed to load candles for {requestTicker}: {ex.Message}");
                return [];
            }

            if (candles == null || candles.Count == 0)
            {
                services.Logger.Info($"Skipping {originalTicker} — no market data");
                return [];
            }

            var rows = services.DatasetBuilder.Build(tickerTrades, candles);

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
    services.Logger.Info("Candidate search not implemented yet");
}