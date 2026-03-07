using System.Globalization;
using IbSwingTrader.Models;

namespace IbSwingTrader.MarketData.Csv
{
    public class CsvTradeReader
    {
        public static List<TradeRecord> Read(string path)
        {
            if (!File.Exists(path))
                throw new Exception($"CSV file not found: {path}");

            var trades = new List<TradeRecord>();

            var lines = File.ReadAllLines(path);

            if (lines.Length <= 1)
                throw new Exception("CSV file is empty");

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(';');

                try
                {
                    var ticker = parts[2];

                    var entryDate = parts[3];
                    var entryTime = parts[4];
                    var entryPrice = parts[6];

                    var exitDate = parts[14];
                    var exitTime = parts[15];
                    var exitPrice = parts[17];

                    var profitPercent = parts[1];
                    var holdDays = parts[0];

                    var entryDateTime = DateTime.Parse($"{entryDate} {entryTime}");
                    var exitDateTime = DateTime.Parse($"{exitDate} {exitTime}");

                    var trade = new TradeRecord
                    {
                        Ticker = ticker,

                        EntryTime = entryDateTime,
                        EntryPrice = decimal.Parse(entryPrice, CultureInfo.InvariantCulture),

                        ExitTime = exitDateTime,
                        ExitPrice = decimal.Parse(exitPrice, CultureInfo.InvariantCulture),

                        ProfitPercent = ParsePercent(profitPercent),

                        HoldDays = int.Parse(holdDays)
                    };

                    trades.Add(trade);
                }
                catch (Exception ex)
                {
                    throw new Exception($"CSV parse error at line {i + 1}: {ex.Message}");
                }
            }

            return trades;
        }

        private static decimal ParsePercent(string value)
        {
            value = value.Replace("%", "").Trim();
            return decimal.Parse(value, CultureInfo.InvariantCulture);
        }
    }
}
