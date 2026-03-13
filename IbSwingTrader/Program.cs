using IbSwingTrader.Analysis;
using IbSwingTrader.Bootstrap;
using IbSwingTrader.MarketData.Csv;
using IbSwingTrader.MarketData.IB;
using IbSwingTrader.Models;

var services = ConfigureServices();

if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  build-dataset <trades.csv> <dataset.csv>");
    Console.WriteLine("  get-candidates");
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
        Console.WriteLine("Unknown command");
        break;
}

static Services ConfigureServices()
{
    return new Services
    {
        DatasetBuilder = new TradeDatasetBuilder(),
        CsvWriter = new CsvDatasetWriter()
    };
}

async Task RunBuildDataset(string[] args, Services services)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Usage: build-dataset <trades.csv> <dataset.csv>");
        return;
    }

    var tradesPath = args[1];
    var datasetPath = args[2];

    var trades = CsvTradeReader.Read(tradesPath);

    if (trades.Count == 0)
    {
        Console.WriteLine("No trades found");
        return;
    }

    var grouped = trades
        .GroupBy(t => t.Ticker)
        .OrderBy(g => g.Key)
        .ToList();

    Console.WriteLine($"Tickers found: {grouped.Count}");

    var tws = new TwsConnection();
    tws.Connect();

    await tws.Ready.Task;

    var marketData = new TwsMarketDataProvider(tws);

    var semaphore = new SemaphoreSlim(4);
    var tasks = new List<Task<List<TradeDatasetRow>>>();

    foreach (var g in grouped)
    {
        tasks.Add(ProcessTicker(g));
    }

    var results = await Task.WhenAll(tasks);

    var allRows = results.SelectMany(r => r).ToList();

    Console.WriteLine();
    Console.WriteLine($"Total dataset rows: {allRows.Count}");

    services.CsvWriter.Write(datasetPath, allRows);

    Console.WriteLine($"Dataset saved: {datasetPath}");

    async Task<List<TradeDatasetRow>> ProcessTicker(IGrouping<string, TradeRecord> g)
    {
        await semaphore.WaitAsync();

        try
        {
            var ticker = g.Key;
            var tickerTrades = g.ToList();

            var earliest = tickerTrades.Min(t => t.EntryTimeUtc);
            var latest = tickerTrades.Max(t => t.ExitTimeUtc);

            var start = earliest.AddDays(-20);

            Console.WriteLine();
            Console.WriteLine($"Ticker: {ticker}");
            Console.WriteLine($"Trades: {tickerTrades.Count}");
            Console.WriteLine($"Range: {start:yyyy-MM-dd} -> {latest:yyyy-MM-dd}");

            List<Candle> candles;

            try
            {
                candles = await marketData.GetHistoricalRange(
                    ticker,
                    Timeframe.H4,
                    start,
                    latest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load candles for {ticker}: {ex.Message}");
                return new List<TradeDatasetRow>();
            }

            if (candles == null || candles.Count == 0)
            {
                Console.WriteLine($"Skipping {ticker} — no market data");
                return [];
            }

            var rows = services.DatasetBuilder.Build(tickerTrades, candles);

            Console.WriteLine($"Rows built: {rows.Count}");

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
    Console.WriteLine("Candidate search not implemented yet");
}