using IbSwingTrader.MarketData.Csv;
using IbSwingTrader.MarketData.IB;

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
        RunTwsConnection();
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

static void RunTwsConnection()
{
    var tws = new TwsConnection();

    tws.Connect();

    Console.WriteLine("Connected: " + tws.IsConnected);

    Console.ReadLine();
}