using IbSwingTrader.MarketData.Csv;
using IbSwingTrader.MarketData.IB;
using IbSwingTrader.Models;

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
        await RunBuildDataset(args);
        break;

    case "get-candidates":
        await RunGetCandidates();
        break;

    default:
        Console.WriteLine("Unknown command");
        break;
}

async Task RunBuildDataset(string[] args)
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

    var ticker = trades
        .GroupBy(t => t.Ticker)
        .Select(g => new
        {
            Ticker = g.Key,
            Range = g.Max(x => x.ExitTimeUtc) - g.Min(x => x.EntryTimeUtc)
        })
        .OrderByDescending(x => x.Range)
        .First()
        .Ticker;

    var tickerTrades = trades.Where(t => t.Ticker == ticker).ToList();

    var earliest = tickerTrades.Min(t => t.EntryTimeUtc);
    var latest = tickerTrades.Max(t => t.ExitTimeUtc);

    var start = earliest.AddDays(-20);

    Console.WriteLine($"Ticker: {ticker}");
    Console.WriteLine($"Range: {start:yyyy-MM-dd} -> {latest:yyyy-MM-dd}");

    var tws = new TwsConnection();
    tws.Connect();

    await tws.Ready.Task;

    var marketData = new TwsMarketDataProvider(tws);

    var candles = await marketData.GetCandles(
        ticker,
        Timeframe.H4,
        latest,
        2000);

    using var writer = new StreamWriter(datasetPath);

    writer.WriteLine("Ticker;TimeUtc;Open;High;Low;Close;Volume");

    foreach (var c in candles)
    {
        writer.WriteLine($"{ticker};{c.Time:yyyy-MM-dd HH:mm:ss};{c.Open};{c.High};{c.Low};{c.Close};{c.Volume}");
    }

    Console.WriteLine($"Dataset saved: {datasetPath}");
}

async Task RunGetCandidates()
{
    Console.WriteLine("Candidate search not implemented yet");
}