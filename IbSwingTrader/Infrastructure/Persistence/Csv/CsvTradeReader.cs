using System.Globalization;

namespace IbSwingTrader.Infrastructure.Persistence.Csv
{
    public class CsvTradeReader(
        IMarketSettingsProvider marketSettingsProvider,
        ICsvTradeReaderSettingsProvider settingsProvider) : ICsvTradeReader
    {
        private readonly IMarketSettingsProvider _marketSettingsProvider = marketSettingsProvider;
        private readonly ICsvTradeReaderSettingsProvider _settingsProvider = settingsProvider;

        public List<TradeRecord> Read(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"CSV file not found: {path}", path);

            var settings = _settingsProvider.Get();
            var map = BuildMap(settings);

            var lines = File.ReadAllLines(path);

            if (lines.Length == 0)
                throw new InvalidOperationException("CSV file is empty.");

            var startRow = settings.HasHeader ? 1 : 0;
            var trades = new List<TradeRecord>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicateCount = 0;

            for (int rowIndex = startRow; rowIndex < lines.Length; rowIndex++)
            {
                var line = lines[rowIndex];

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(settings.Separator);

                try
                {
                    if (IsSkippableRow(parts, map))
                        continue;

                    var ticker = Get(parts, map, "Ticker", rowIndex)
                        .Trim()
                        .ToUpperInvariant();

                    var holdDays = int.Parse(
                        Get(parts, map, "HoldDays", rowIndex).Trim(),
                        CultureInfo.InvariantCulture);

                    var profitPercent = ParsePercent(
                        Get(parts, map, "ProfitPercent", rowIndex));

                    var entryDate = Get(parts, map, "EntryDate", rowIndex).Trim();
                    var entryTime = Get(parts, map, "EntryTime", rowIndex).Trim();
                    var entryPrice = ParseMoney(
                        Get(parts, map, "EntryPrice", rowIndex));

                    var exitDate = Get(parts, map, "ExitDate", rowIndex).Trim();
                    var exitTime = Get(parts, map, "ExitTime", rowIndex).Trim();
                    var exitPrice = ParseMoney(
                        Get(parts, map, "ExitPrice", rowIndex));

                    var isShortRaw = Get(parts, map, "IsShort", rowIndex).Trim();
                    var isShort = ParseIsShort(isShortRaw);

                    var entryLocal = DateTime.ParseExact(
                        $"{entryDate} {entryTime}",
                        "dd.MM.yyyy H:mm:ss",
                        CultureInfo.InvariantCulture);

                    var exitLocal = DateTime.ParseExact(
                        $"{exitDate} {exitTime}",
                        "dd.MM.yyyy H:mm:ss",
                        CultureInfo.InvariantCulture);

                    var trade = new TradeRecord
                    {
                        Ticker = ticker,
                        IsShort = isShort,
                        EntryTimeMarket = entryLocal,
                        EntryPrice = entryPrice,
                        ExitTimeMarket = exitLocal,
                        ExitPrice = exitPrice,
                        ProfitPercent = profitPercent,
                        HoldDays = holdDays
                    };

                    var dedupKey = BuildDedupKey(trade);

                    if (!seen.Add(dedupKey))
                    {
                        duplicateCount++;
                        continue;
                    }

                    trades.Add(trade);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"CSV parse error at line {rowIndex + 1}: {ex.Message}",
                        ex);
                }
            }

            if (duplicateCount > 0)
            {
                Console.WriteLine(
                    $"CsvTradeReader: skipped duplicate trades: {duplicateCount}");
            }

            return trades
                .OrderBy(x => x.Ticker)
                .ThenBy(x => x.EntryTimeMarket)
                .ToList();
        }

        private static Dictionary<string, int> BuildMap(CsvTradeReaderSettings settings)
        {
            var map = settings.Columns.ToDictionary(
                x => x.Name,
                x => x.Index,
                StringComparer.OrdinalIgnoreCase);

            var requiredFields = new[]
            {
                "HoldDays",
                "ProfitPercent",
                "Ticker",
                "EntryDate",
                "EntryTime",
                "EntryPrice",
                "ExitDate",
                "ExitTime",
                "ExitPrice",
                "IsShort"
            };

            foreach (var field in requiredFields)
            {
                if (!map.ContainsKey(field))
                {
                    throw new InvalidOperationException(
                        $"CsvTradeReader column mapping is missing required field '{field}'.");
                }
            }

            return map;
        }

        private static bool IsSkippableRow(
            string[] parts,
            Dictionary<string, int> map)
        {
            var ticker = SafeGet(parts, map, "Ticker");
            var holdDays = SafeGet(parts, map, "HoldDays");

            return string.IsNullOrWhiteSpace(ticker)
                   || string.IsNullOrWhiteSpace(holdDays);
        }

        private static string Get(
            string[] parts,
            Dictionary<string, int> map,
            string fieldName,
            int rowIndex)
        {
            var index = map[fieldName];

            if (index < 0 || index >= parts.Length)
            {
                throw new IndexOutOfRangeException(
                    $"Column '{fieldName}' points to index {index}, but line {rowIndex + 1} contains only {parts.Length} column(s).");
            }

            return parts[index];
        }

        private static string? SafeGet(
            string[] parts,
            Dictionary<string, int> map,
            string fieldName)
        {
            if (!map.TryGetValue(fieldName, out var index))
                return null;

            if (index < 0 || index >= parts.Length)
                return null;

            return parts[index];
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

        private static bool ParseIsShort(string value)
        {
            return value switch
            {
                "1" => true,
                "0" => false,
                _ => throw new FormatException($"Invalid IsShort value '{value}'. Expected 0 or 1.")
            };
        }

        private static string BuildDedupKey(TradeRecord trade)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{trade.Ticker}|{trade.IsShort}|{trade.EntryTimeMarket:yyyyMMddHHmmss}|{trade.ExitTimeMarket:yyyyMMddHHmmss}|{trade.EntryPrice:F4}|{trade.ExitPrice:F4}");
        }
    }
}
