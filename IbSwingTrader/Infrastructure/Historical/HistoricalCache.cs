using System.Text.Json;
using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Infrastructure.Historical
{
    public class HistoricalCache : IHistoricalCache
    {
        private readonly string _cacheDir;

        public HistoricalCache(string cacheDir = "cache")
        {
            _cacheDir = cacheDir;
            Directory.CreateDirectory(_cacheDir);
        }

        private string GetPath(string symbol)
        {
            return Path.Combine(_cacheDir, $"{symbol}.json");
        }

        public bool TryLoad(string symbol, out List<Candle>? candles)
        {
            var path = GetPath(symbol);

            if (!File.Exists(path))
            {
                candles = null;
                return false;
            }

            var json = File.ReadAllText(path);
            candles = JsonSerializer.Deserialize<List<Candle>>(json);

            return candles != null;
        }

        public void Save(string symbol, List<Candle> candles)
        {
            var json = JsonSerializer.Serialize(candles);
            File.WriteAllText(GetPath(symbol), json);
        }
    }
}