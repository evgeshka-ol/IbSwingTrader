using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Historical
{
    public class FileHistoricalCache : IHistoricalCache
    {
        private readonly string _folder;
        private readonly ITextLogger _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public FileHistoricalCache(string folder, ITextLogger logger)
        {
            _folder = folder;
            _logger = logger;

            Directory.CreateDirectory(_folder);
        }

        public bool TryLoad(string key, out List<Candle> candles)
        {
            candles = [];

            var path = BuildPath(key);

            if (!File.Exists(path))
                return false;

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<List<Candle>>(json, JsonOptions);

                if (data == null)
                    return false;

                candles = data;
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"Historical cache load failed: {path}. {ex.Message}");
                return false;
            }
        }

        public void Save(string key, List<Candle> candles)
        {
            var path = BuildPath(key);

            try
            {
                var json = JsonSerializer.Serialize(candles, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                _logger.Error($"Historical cache save failed: {path}. {ex.Message}");
            }
        }

        private string BuildPath(string key)
        {
            var safeFileName = BuildSafeFileName(key);
            return Path.Combine(_folder, safeFileName);
        }

        private static string BuildSafeFileName(string key)
        {
            var readablePrefix = BuildReadablePrefix(key);
            var hash = ComputeSha256(key);

            return $"{readablePrefix}_{hash}.json";
        }

        private static string BuildReadablePrefix(string key)
        {
            var parts = key.Split('|', StringSplitOptions.RemoveEmptyEntries);

            var symbol = parts.Length > 0 ? parts[0] : "unknown";
            var timeframe = parts.Length > 1 ? parts[1] : "tf";

            symbol = SanitizeFileNamePart(symbol);
            timeframe = SanitizeFileNamePart(timeframe);

            return $"{symbol}_{timeframe}";
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

        private static string ComputeSha256(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);

            var sb = new StringBuilder(hash.Length * 2);

            foreach (var b in hash)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }
    }
}