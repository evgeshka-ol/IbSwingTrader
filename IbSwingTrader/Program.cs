using System.Globalization;
using System.Text.Json;
using IbSwingTrader.MarketData.Csv;
using IbSwingTrader.MarketData.IB;
using IbSwingTrader.Models;

// Cache JsonSerializerOptions to avoid creating new instance for every serialization
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true
};

if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  parse-csv <file>");
    Console.WriteLine("  connect");
    return;
}

var command = args[0];

switch (command)
{
    case "parse-csv":
        RunCsvParser(args);
        break;

    case "connect":
        await RunTwsConnection();
        break;

    default:
        Console.WriteLine("Unknown command");
        break;
}

void RunCsvParser(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("CSV file path required");
        return;
    }

    var path = args[1];

    var trades = CsvTradeReader.Read(path);
    using var writer = new StreamWriter("trades_sorted.csv");

    writer.WriteLine("Ticker,EntryTimeUtc,ExitTimeUtc");

    foreach (var t in trades)
    {
        writer.WriteLine($"{t.Ticker},{t.EntryTimeUtc:yyyy-MM-dd HH:mm:ss},{t.ExitTimeUtc:yyyy-MM-dd HH:mm:ss}");
    }

    Console.WriteLine($"Trades loaded: {trades.Count}");

    if (trades.Count > 0)
    {
        var json = JsonSerializer.Serialize(
            trades[0],
            jsonOptions);

        Console.WriteLine("First trade parsed:");
        Console.WriteLine(json);
    }
}

static async Task RunTwsConnection()
{
    var tws = new TwsConnection();

    tws.Connect();

    Console.WriteLine("Connected: " + tws.IsConnected);

    // ждём готовность API
    await tws.Ready.Task;

    var marketData = new TwsMarketDataProvider(tws);
    var candles = await marketData.GetCandles(
        "AAPL",
        Timeframe.M5,
        DateTime.UtcNow,
        10);

    Console.WriteLine($"Candles received: {candles.Count}");

    foreach (var c in candles)
    {
        Console.WriteLine($"{c.Time:HH:mm} O:{c.Open} H:{c.High} L:{c.Low} C:{c.Close} V:{c.Volume}");
    }

    Console.ReadLine();
}