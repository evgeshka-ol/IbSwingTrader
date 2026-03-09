using IbSwingTrader.MarketData.Csv;
using IbSwingTrader.MarketData.IB;
using IbSwingTrader.Models;

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

static void RunCsvParser(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("CSV file path required");
        return;
    }

    var path = args[1];

    var trades = CsvTradeReader.Read(path);

    Console.WriteLine($"Trades loaded: {trades.Count}");

    if (trades.Count > 0)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            trades[0],
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

        Console.WriteLine("First trade parsed:");
        Console.WriteLine(json);
    }
}

static async Task RunTwsConnection()
{
    var tws = new TwsConnection();

    tws.Connect();

    Console.WriteLine("Connected: " + tws.IsConnected);

    // создаём провайдер
    var marketData = new TwsMarketDataProvider(tws.Client);

    // дать TWS время установить соединение
    await Task.Delay(2000);

    var candles = await marketData.GetCandles(
        "AAPL",
        Timeframe.M5,
        DateTime.UtcNow,
        10);

    Console.WriteLine($"Candles received: {candles.Count}");

    foreach (var c in candles)
    {
        Console.WriteLine(
            $"{c.Time:HH:mm} O:{c.Open} H:{c.High} L:{c.Low} C:{c.Close} V:{c.Volume}");
    }

    Console.ReadLine();
}