using System.Collections.Concurrent;
using System.Globalization;
using IBApi;

namespace IbSwingTrader.Infrastructure.Brokers.InteractiveBrokers
{
    public partial class TwsConnection
    {
        private sealed class LiveQuoteRequest(int maxAgeSeconds, decimal maxSpreadPct)
        {
            public readonly TaskCompletionSource<LiveQuote?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int DataType;
            public decimal? Bid, Ask;
            public DateTime BidAt, AskAt;
            public LiveQuote? Trade;

            public LiveQuote? Select(DateTime now)
            {
                if (DataType != 1) return null;
                var oldest = now.AddSeconds(-Math.Max(1, maxAgeSeconds));
                if (Bid > 0m && Ask > 0m && BidAt >= oldest && AskAt >= oldest)
                {
                    if (Ask < Bid || (Ask.Value - Bid.Value) / Bid.Value * 100m > Math.Max(0m, maxSpreadPct))
                        return null; // A trade must not bypass a known crossed/wide book.
                    // Top-of-book has no exchange timestamp. Explicitly record receipt time.
                    return new LiveQuote(Ask.Value, AskAt, now, "IBLiveAskObserved");
                }
                return Trade is { } trade && trade.PriceTimeUtc >= oldest &&
                    trade.PriceTimeUtc <= now.AddSeconds(2) ? trade : null;
            }
        }

        private readonly ConcurrentDictionary<int, LiveQuoteRequest> _liveQuotes = new();

        public async Task<LiveQuote?> GetLiveQuoteAsync(Contract contract, int timeoutSeconds,
            int maxAgeSeconds, decimal maxSpreadPct)
        {
            if (!Client.IsConnected() || !_ibConnected) return null;
            var id = Interlocked.Increment(ref _nextRequestId);
            var request = new LiveQuoteRequest(maxAgeSeconds, maxSpreadPct);
            _liveQuotes[id] = request;
            _logger.Info($"Live quote fallback request: {contract.Symbol}, ReqId={id}, TimeoutSeconds={timeoutSeconds}");
            try
            {
                Client.reqMarketDataType(1);
                // Short-lived streaming request, no regulatory snapshot.
                // RTVolume provides atomic trade price + exchange trade timestamp.
                Client.reqMktData(id, contract, "233", false, false, null);
                var done = await Task.WhenAny(request.Completion.Task,
                    Task.Delay(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 30))));
                LiveQuote? result;
                lock (request)
                {
                    result = done == request.Completion.Task
                        ? request.Completion.Task.GetAwaiter().GetResult() : request.Select(DateTime.UtcNow);
                    if (result != null && (request.DataType != 1 ||
                        DateTime.UtcNow - result.PriceTimeUtc > TimeSpan.FromSeconds(Math.Max(1, maxAgeSeconds))))
                        result = null;
                }
                _logger.Info($"Live quote fallback result: {contract.Symbol}, DataType={request.DataType}, " +
                    $"Source={result?.Source ?? "unavailable"}, Price={result?.Price.ToString(CultureInfo.InvariantCulture) ?? "none"}, " +
                    $"PriceTimeUtc={result?.PriceTimeUtc:O}, ObservedAtUtc={result?.ObservedAtUtc:O}");
                return result;
            }
            finally
            {
                _liveQuotes.TryRemove(id, out _);
                try { Client.cancelMktData(id); }
                catch (Exception ex) { _logger.Info($"Live quote cancel failed: {contract.Symbol}. {ex.Message}"); }
            }
        }

        private void ReceiveLivePrice(int id, int field, double value, TickAttrib attributes)
        {
            if (!_liveQuotes.TryGetValue(id, out var request) || field is not (1 or 2)) return;
            lock (request)
            {
                decimal? price = double.IsFinite(value) && value > 0 && value < (double)decimal.MaxValue &&
                    !attributes.PastLimit ? (decimal)value : null;
                if (field == 1) { request.Bid = price; request.BidAt = DateTime.UtcNow; }
                else { request.Ask = price; request.AskAt = DateTime.UtcNow; }
                // Prefer a valid book; wait until timeout before a trade-only fallback.
                if (request.Select(DateTime.UtcNow) is { Source: "IBLiveAskObserved" } quote)
                    request.Completion.TrySetResult(quote);
            }
        }

        private void ReceiveLiveTrade(int id, int field, string value)
        {
            if (field != 48 || !_liveQuotes.TryGetValue(id, out var request)) return;
            var parts = value.Split(';');
            if (parts.Length < 3 || !decimal.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var price) ||
                price <= 0m || !long.TryParse(parts[2], out var milliseconds)) return;
            DateTime tradeAt;
            try { tradeAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime; }
            catch (ArgumentOutOfRangeException) { return; }
            lock (request)
                request.Trade = new LiveQuote(price, tradeAt, DateTime.UtcNow, "IBLiveLastTrade");
        }

        private void ReceiveLiveDataType(int id, int type)
        {
            if (!_liveQuotes.TryGetValue(id, out var request)) return;
            lock (request)
            {
                request.DataType = type;
                if (type != 1) request.Completion.TrySetResult(null);
                else if (request.Select(DateTime.UtcNow) is { Source: "IBLiveAskObserved" } quote)
                    request.Completion.TrySetResult(quote);
            }
        }

        private void ReceiveLiveError(int id, int code, string message)
        {
            if (!_liveQuotes.TryGetValue(id, out var request)) return;
            _logger.Info($"Live quote fallback IB response: ReqId={id}, Code={code}, Message={message}");
            if (code is 200 or 354 or 10167 or 10168 or 10089 or 10091)
                request.Completion.TrySetResult(null);
        }
    }
}
