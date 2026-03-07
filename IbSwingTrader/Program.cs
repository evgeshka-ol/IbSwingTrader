using IbSwingTrader.MarketData.Csv;
using System.Text.Json;

Console.WriteLine("IbSwingTrader");

if (args.Length == 0)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("IbSwingTrader <csvFilePath>");
    return;
}

var file = args[0];

try
{
    var trades = CsvTradeReader.Read(file);

    Console.WriteLine($"Trades loaded: {trades.Count}");

    if (trades.Count == 0)
    {
        Console.WriteLine("No trades found");
        return;
    }

    var firstTrade = trades[0];

    var json = JsonSerializer.Serialize(
        firstTrade,
        new JsonSerializerOptions
        {
            WriteIndented = true
        });

    Console.WriteLine("First trade parsed:");
    Console.WriteLine(json);
}
catch (Exception ex)
{
    Console.WriteLine("Error:");
    Console.WriteLine(ex.Message);
}