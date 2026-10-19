using System.Text.Json;
using IbSwingTrader.Common.Time;

namespace IbSwingTrader.Application.WishList
{
    public sealed class PendingReversalWatchCheck
    {
        public string Ticker { get; set; } = string.Empty;
        public int ConId { get; set; }
        public string Exchange { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public string TradingClass { get; set; } = string.Empty;
        public string StockType { get; set; } = string.Empty;
        public string ScanCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime SeenAt { get; set; }
        public DateTime NextAttemptAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool ValidateExisting { get; set; }
        public string Reason { get; set; } = string.Empty;

        public StockInfo ToStock() => new()
        {
            Ticker = Ticker, ConId = ConId, Exchange = Exchange, Currency = Currency,
            TradingClass = TradingClass, StockType = StockType
        };
    }

    // Pending observations are not active watches or authorization to publish a trade.
    public sealed class ReversalWatchPendingChecks(string path)
    {
        private Dictionary<string, PendingReversalWatchCheck> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _attempts = new(StringComparer.OrdinalIgnoreCase);
        public int Count => _items.Count;

        public async Task LoadAsync(DateTime now)
        {
            _attempts.Clear();
            var saved = File.Exists(path)
                ? JsonSerializer.Deserialize<List<PendingReversalWatchCheck>>(await File.ReadAllTextAsync(path))
                    ?? throw new InvalidDataException($"Invalid pending reversal checks: {path}")
                : [];
            if (saved.Any(x => string.IsNullOrWhiteSpace(x.Ticker) || string.IsNullOrWhiteSpace(x.ScanCode) ||
                x.SeenAt == default || x.NextAttemptAt == default || x.ExpiresAt <= x.SeenAt))
                throw new InvalidDataException($"Invalid pending reversal check fields: {path}");
            _items = saved.Where(x => x.ExpiresAt > now).GroupBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.SeenAt).First(), StringComparer.OrdinalIgnoreCase);
            if (saved.Count != _items.Count)
                await SaveAsync();
        }

        public IEnumerable<PendingReversalWatchCheck> Due(DateTime now) => _items.Values
            .Where(x => x.NextAttemptAt <= now && x.ExpiresAt > now && _attempts.GetValueOrDefault(x.Ticker) < 3)
            .OrderBy(x => x.NextAttemptAt).ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase).ToList();

        public bool BeginAttempt(string ticker, DateTime now)
        {
            if (!_items.TryGetValue(ticker, out var item) || item.NextAttemptAt > now || item.ExpiresAt <= now ||
                _attempts.GetValueOrDefault(ticker) >= 3)
                return false;
            _attempts[ticker] = _attempts.GetValueOrDefault(ticker) + 1;
            item.NextAttemptAt = NextBucket(now);
            return true;
        }

        public async Task DeferAsync(StockInfo stock, PresetScanCode preset, DateTime seenAt,
            DateTime retryAt, DateTime expiresAt, bool validateExisting, string reason)
        {
            _items[stock.Ticker] = new PendingReversalWatchCheck
            {
                Ticker = stock.Ticker, ConId = stock.ConId, Exchange = stock.Exchange,
                Currency = stock.Currency, TradingClass = stock.TradingClass, StockType = stock.StockType ?? string.Empty,
                ScanCode = preset.ScanCode, Description = preset.Description, SeenAt = seenAt,
                NextAttemptAt = retryAt, ExpiresAt = expiresAt, ValidateExisting = validateExisting, Reason = reason
            };
            await SaveAsync();
        }

        public async Task CompleteAsync(string ticker)
        {
            if (_items.Remove(ticker)) await SaveAsync();
        }

        private async Task SaveAsync()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_items.Values.ToList()));
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static DateTime NextBucket(DateTime now) =>
            now.AddTicks(-(now.Ticks % TimeSpan.FromMinutes(5).Ticks)).AddMinutes(5);

        public static (bool Open, DateTime RetryAt, DateTime ExpiresAt)? Window(
            MarketSessionSchedule schedule, DateTime now, string timezone)
        {
            var sessions = schedule.Days.Where(x => x.IsTradingDay).SelectMany(x => x.Sessions)
                .Where(x => x.Scope == MarketSessionScope.TradingHours)
                .Select(x => (Start: MarketTime.FromUtc(x.StartUtc, timezone), End: MarketTime.FromUtc(x.EndUtc, timezone)))
                .Where(x => x.End > now).OrderBy(x => x.Start).ToList();
            if (sessions.Count == 0) return null;
            var first = sessions[0];
            var expiry = sessions.Where(x => x.Start.Date == first.Start.Date).Max(x => x.End);
            return (first.Start <= now, first.Start > now ? first.Start.AddMinutes(5) : NextBucket(now), expiry);
        }
    }
}
