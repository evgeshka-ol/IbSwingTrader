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

    Console.WriteLine($"Tickers: {grouped.Count}");

    var tws = new TwsConnection();
    tws.Connect();

    await tws.Ready.Task;

    var marketData = new TwsMarketDataProvider(tws);

    var allRows = new List<TradeDatasetRow>();

    foreach (var g in grouped)
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

        try
        {
            var candles = await marketData.GetHistoricalRange(
                ticker,
                Timeframe.H4,
                start,
                latest);

            if (candles.Count == 0)
            {
                Console.WriteLine("No candles received");
                continue;
            }

            var rows = services.DatasetBuilder.Build(tickerTrades, candles);

            Console.WriteLine($"Dataset rows: {rows.Count}");

            allRows.AddRange(rows);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to process {ticker}: {ex.Message}");
        }

        // защита от pacing violation IBKR
        await Task.Delay(500);
    }

    Console.WriteLine();
    Console.WriteLine($"Total dataset rows: {allRows.Count}");

    services.CsvWriter.Write(datasetPath, allRows);

    Console.WriteLine($"Dataset saved: {datasetPath}");
}

async Task RunGetCandidates()
{
    Console.WriteLine("Candidate search not implemented yet");
}