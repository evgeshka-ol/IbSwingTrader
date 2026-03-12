using IbSwingTrader.Analysis;
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

    var candles = await marketData.GetHistoricalRange(
        ticker,
        Timeframe.H4,
        start,
        latest);

    var rows = TradeDatasetBuilder.Build(tickerTrades, candles);

    using var writer = new StreamWriter(datasetPath);

    writer.WriteLine(
        "Ticker;EntryShiftBars;EntryTimeUtc;EntryPrice;ExitTimeUtc;ExitPrice;ProfitPercent;HoldDays;IsRealTrade;" +
        "Pullback5d;Pullback10d;VolumeRatio20;TrendPosition;" +
        "FutureHigh1d;FutureLow1d;FutureHigh2d;FutureLow2d;MaxReturn1d;MaxReturn2d;MaxDrawdown1d;MaxDrawdown2d;Target10pct1d;Target10pct2d");

    foreach (var r in rows)
    {
        writer.WriteLine(
            $"{r.Ticker};" +
            $"{r.EntryShiftBars};" +
            $"{r.EntryTimeUtc:yyyy-MM-dd HH:mm:ss};" +
            $"{r.EntryPrice};" +
            $"{r.ExitTimeUtc:yyyy-MM-dd HH:mm:ss};" +
            $"{r.ExitPrice};" +
            $"{r.ProfitPercent};" +
            $"{r.HoldDays};" +
            $"{r.IsRealTrade};" +
            $"{r.Pullback5d};" +
            $"{r.Pullback10d};" +
            $"{r.VolumeRatio20};" +
            $"{r.TrendPosition};" +
            $"{r.FutureHigh1d};" +
            $"{r.FutureLow1d};" +
            $"{r.FutureHigh2d};" +
            $"{r.FutureLow2d};" +
            $"{r.MaxReturn1d};" +
            $"{r.MaxReturn2d};" +
            $"{r.MaxDrawdown1d};" +
            $"{r.MaxDrawdown2d};" +
            $"{r.Target10pct1d};" +
            $"{r.Target10pct2d}");
    }

    Console.WriteLine($"Dataset saved: {datasetPath}");
}

async Task RunGetCandidates()
{
    Console.WriteLine("Candidate search not implemented yet");
}