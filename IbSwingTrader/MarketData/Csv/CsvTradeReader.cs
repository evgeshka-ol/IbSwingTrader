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
                    var entryPrice = ParseMoney(parts[6]);

                    var exitDate = parts[14];
                    var exitTime = parts[15];
                    var exitPrice = ParseMoney(parts[17]);

                    var profitPercent = ParsePercent(parts[1]);
                    var holdDays = int.Parse(parts[0]);

                    var entryDateTime = DateTime.ParseExact(
                        $"{entryDate} {entryTime}",
                        "dd.MM.yyyy H:mm:ss",
                        CultureInfo.InvariantCulture);

                    var exitDateTime = DateTime.ParseExact(
                        $"{exitDate} {exitTime}",
                        "dd.MM.yyyy H:mm:ss",
                        CultureInfo.InvariantCulture);

                    var trade = new TradeRecord
                    {
                        Ticker = ticker,

                        EntryTime = entryDateTime,
                        EntryPrice = entryPrice,

                        ExitTime = exitDateTime,
                        ExitPrice = exitPrice,

                        ProfitPercent = profitPercent,
                        HoldDays = holdDays
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

        private static decimal ParseMoney(string value)
        {
            value = value.Replace("$", "").Trim();
            value = value.Replace(",", ".");
            return decimal.Parse(value, CultureInfo.InvariantCulture);
        }

        private static decimal ParsePercent(string value)
        {
            value = value.Replace("%", "").Trim();
            value = value.Replace(",", ".");
            return decimal.Parse(value, CultureInfo.InvariantCulture);
        }
    }
}
