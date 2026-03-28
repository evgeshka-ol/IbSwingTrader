using System.Text;
using System.Text.Json;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Infrastructure.Historical
{
    public class HistoricalCache : IHistoricalCache
    {
        private readonly string _folder;
        private readonly ITextLogger _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public HistoricalCache(
            IAgentPathService pathService,
            ITextLogger logger)
        {
            _folder = pathService.GetCacheFolder();
            _logger = logger;

            Directory.CreateDirectory(_folder);
        }

        public bool TryLoad(
            string symbol,
            Timeframe timeframe,
            out List<Candle>? candles)
        {
            candles = null;

            var path = BuildPath(symbol);

            if (!File.Exists(path))
                return false;

            try
            {
                var json = File.ReadAllText(path);
                var file = JsonSerializer.Deserialize<HistoricalSymbolCache>(json, JsonOptions);

                if (file == null)
                    return false;

                var timeframeKey = BuildTimeframeKey(timeframe);

                if (!file.Timeframes.TryGetValue(timeframeKey, out var storedCandles) ||
                    storedCandles == null ||
                    storedCandles.Count == 0)
                {
                    return false;
                }

                candles = storedCandles
                    .Select(NormalizeCandleTime)
                    .OrderBy(x => x.Time)
                    .ToList();

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Historical cache load failed: {path}. {ex.Message}");
                return false;
            }
        }

        public void Save(
            string symbol,
            Timeframe timeframe,
            List<Candle> candles)
        {
            var path = BuildPath(symbol);

            try
            {
                HistoricalSymbolCache file;

                if (File.Exists(path))
                {
                    var existingJson = File.ReadAllText(path);
                    file = JsonSerializer.Deserialize<HistoricalSymbolCache>(existingJson, JsonOptions)
                           ?? new HistoricalSymbolCache();
                }
                else
                {
                    file = new HistoricalSymbolCache();
                }

                file.Symbol = symbol;

                var timeframeKey = BuildTimeframeKey(timeframe);

                file.Timeframes[timeframeKey] = candles
                    .Select(NormalizeCandleTime)
                    .GroupBy(x => new { x.Timeframe, x.Time })
                    .Select(g => g.First())
                    .OrderBy(x => x.Time)
                    .ToList();

                var json = JsonSerializer.Serialize(file, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                _logger.Error($"Historical cache save failed: {path}. {ex.Message}");
            }
        }

        private string BuildPath(string symbol)
        {
            var safeSymbol = SanitizeFileNamePart(symbol);
            return Path.Combine(_folder, $"{safeSymbol}.json");
        }

        private static string BuildTimeframeKey(Timeframe timeframe)
        {
            return timeframe.ToString();
        }

        private static string SanitizeFileNamePart(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);

            foreach (var ch in value)
            {
                if (invalidChars.Contains(ch))
                    sb.Append('_');
                else
                    sb.Append(ch);
            }

            return sb.ToString();
        }

        private static Candle NormalizeCandleTime(Candle candle)
        {
            candle.Time = MarketTime.Normalize(candle.Time);
            return candle;
        }
    }
}
