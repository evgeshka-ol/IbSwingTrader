using System.Text.Json;

namespace IbSwingTrader.Application.WishList
{
    // Active drop cycles awaiting ReversalHook. Retired cycles live in a separate ledger.
    public sealed class ReversalWatchList(
        IWishListReader reader, IWishListResultWriter writer, IAgentPathService paths,
        IGetCandidatesSettingsProvider settingsProvider, IMarketSettingsProvider marketSettings,
        ITextLogger logger)
    {
        private readonly Dictionary<string, WishListItem> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _atStart = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, WishListItem> _newSignals = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, ReversalWatchState> _states = new(StringComparer.OrdinalIgnoreCase);
        private string StatePath => Path.ChangeExtension(paths.GetWishListFile(), ".lifecycle.json");
        public decimal MinimumBelowDailyMidPct => Settings.MinimumBelowDailyMidPct;
        private ReversalWatchState State(string ticker)
        {
            if (!_states.TryGetValue(ticker, out var state))
                _states[ticker] = state = new ReversalWatchState();
            state.EnsureIndicatorModel(FeatureEngine.BollingerModel);
            return state;
        }
        public bool IsActive(string ticker) => _items.ContainsKey(ticker);
        public bool HasNewSignal(string ticker) => _newSignals.ContainsKey(ticker);
        public bool NeedsEnrollmentValidation(string ticker) => IsActive(ticker) && !State(ticker).CycleStartedAt.HasValue;
        public bool IsDeepBelowMid(decimal price, decimal mid) =>
            ReversalWatchState.IsDeepBelowMid(price, mid, MinimumBelowDailyMidPct);
        private ReversalWatchListSettings Settings => settingsProvider.Get().ReversalWatchList;
        public bool Enabled => Settings.Enabled;

        public async Task LoadAsync(DateTime now)
        {
            _items.Clear();
            _atStart.Clear();
            _newSignals.Clear();
            _states.Clear();
            if (!Enabled)
                return;
            if (MinimumBelowDailyMidPct <= 0m || MinimumBelowDailyMidPct >= 100m)
                throw new InvalidOperationException("ReversalWatchList.MinimumBelowDailyMidPct must be between 0 and 100 (exclusive).");
            if (File.Exists(StatePath))
            {
                var saved = JsonSerializer.Deserialize<Dictionary<string, ReversalWatchState>>(
                    await File.ReadAllTextAsync(StatePath))
                    ?? throw new InvalidDataException($"Invalid reversal watch state: {StatePath}");
                _states = new(saved, StringComparer.OrdinalIgnoreCase);
            }
            var loaded = await reader.ReadAsync(paths.GetWishListFile());
            foreach (var item in loaded.Where(x => !string.IsNullOrWhiteSpace(x.Ticker)))
            {
                if (!IsDropCode(item.Scan.PresetScanCode))
                {
                    logger.Info($"Reversal watch discarded legacy non-loser row: {item.Ticker}");
                    continue;
                }
                if (State(item.Ticker).AwaitingNewDrop)
                    continue;
                var lastDrop = item.LastDropSeenAt ?? item.Scan.ScanTime;
                if (string.Equals(item.LastStatus, "Remove", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Settings.RetentionCalendarDays > 0 && lastDrop.Date.AddDays(Settings.RetentionCalendarDays) <= now.Date)
                {
                    logger.Info($"Reversal watch expired: {item.Ticker}, LastDrop={lastDrop:O}");
                    continue;
                }
                item.LastDropSeenAt = lastDrop;
                item.ExpectedTargetTime = null;
                item.ExpectedBarsToTarget = null;
                if (!_items.TryGetValue(item.Ticker, out var previous) || previous.LastDropSeenAt < lastDrop)
                    _items[item.Ticker] = item;
            }
            _atStart.UnionWith(_items.Keys);
            logger.Info($"Reversal watch loaded: {_items.Count}, RetentionCalendarDays={Settings.RetentionCalendarDays}");
        }

        private bool IsDropCode(string code) => Settings.ScanCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
        public bool CapturesCode(string code) => Enabled && IsDropCode(code);
        public bool WasWatchedAtStart(string ticker) => _atStart.Contains(ticker);

        public IEnumerable<(PresetScanCode Preset, List<StockInfo> Stocks)> GetScanBatches()
        {
            var ordered = _items.Values.OrderBy(x => x.LastEvaluatedAt ?? DateTime.MinValue)
                .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase);
            var limit = settingsProvider.Get().MaxWishListItems;
            IEnumerable<WishListItem> selected = limit > 0 ? ordered.Take(limit) : ordered;
            // Fresh signals are a one-run publication queue, not watch overflow.
            selected = selected.Concat(_newSignals.Values).DistinctBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase);
            if (limit > 0 && _items.Count > limit)
                logger.Info($"Reversal watch scan limited: {limit}/{_items.Count}; oldest evaluations first, overflow retained");
            return selected.GroupBy(x => x.Scan.PresetScanCode).Select(group => (
                new PresetScanCode(group.Key, "Persistent reversal watch: " + group.First().Scan.PresetDescription),
                group.Select(item => new StockInfo
                {
                    Ticker = item.Ticker,
                    Currency = string.IsNullOrWhiteSpace(item.WatchCurrency) ? settingsProvider.Get().PreFilter.RequiredCurrency : item.WatchCurrency,
                    StockType = string.IsNullOrWhiteSpace(item.WatchStockType) ? null : item.WatchStockType
                }).ToList())).ToList();
        }

        public bool ObserveDrop(StockInfo stock, PresetScanCode preset, DateTime now,
            decimal currentPrice, decimal closedDailyMid)
        {
            if (!CapturesCode(preset.ScanCode) || !IsDeepBelowMid(currentPrice, closedDailyMid))
                return false;
            var state = State(stock.Ticker);
            if (!state.CanStartNewCycle)
            {
                logger.Info($"Reversal watch enrollment skipped: {stock.Ticker}, Reason=hook-already-observed-awaiting-new-drop");
                return false;
            }
            if (!_items.TryGetValue(stock.Ticker, out var item))
            {
                item = new WishListItem
                {
                    Ticker = stock.Ticker, FirstSeen = now,
                    Scan = new ScanInfo(), Score = new ScoreInfo(), Context = new MarketContextInfo(),
                    LastStatus = "Watching", LastStatusReason = "New loser at least the configured depth below closed Daily mid; waiting for ReversalHook", LastStatusTime = now
                };
                _items[stock.Ticker] = item;
                state.StartCycle(now);
                logger.Info($"Reversal watch enrolled: {stock.Ticker}, Preset={preset.ScanCode}");
            }
            if (!state.CycleStartedAt.HasValue)
                state.StartCycle(now);
            item.FirstSeen ??= now;
            item.LastDropSeenAt = now;
            item.WatchCurrency = stock.Currency;
            item.WatchStockType = stock.StockType ?? string.Empty;
            item.Scan = new ScanInfo
            {
                BollingerModel = FeatureEngine.BollingerModel,
                PresetScanCode = preset.ScanCode, PresetDescription = preset.Description,
                ScanTime = now, ScanTimeZone = marketSettings.Get().Timezone
            };
            item.ExpectedTargetTime = null;
            item.ExpectedBarsToTarget = null;
            return true;
        }

        // A quote outside the deep-drop zone arms a subsequent NEW excursion.
        // This does not enroll a symbol or renew LastDropSeenAt.
        public void ObservePosition(string ticker, DateTime now, decimal price, decimal mid)
        {
            if (price <= 0m || mid <= 0m || MinimumBelowDailyMidPct <= 0m || MinimumBelowDailyMidPct >= 100m)
                return;
            State(ticker).ObservePosition(now, IsDeepBelowMid(price, mid));
        }

        public void ValidateExisting(string ticker, DateTime now, decimal price, decimal mid)
        {
            if (!NeedsEnrollmentValidation(ticker))
                return;
            if (IsDeepBelowMid(price, mid))
                State(ticker).StartCycle(now);
            else
            {
                _items.Remove(ticker);
                logger.Info($"Reversal watch removed legacy shallow drop: {ticker}, Price={price}, DailyMid={mid}, MinimumBelowDailyMidPct={MinimumBelowDailyMidPct}");
            }
        }

        public void InspectHistory(string ticker, List<Candle> daily, List<Candle> h4)
        {
            var state = State(ticker);
            var hadItem = _items.TryGetValue(ticker, out var item);
            var events = new List<(DateTime At, bool Hook, bool Deep, string Frame)>();
            var recentSignal = false;
            void Read(List<Candle> source, Timeframe frame, DateTime? checkedBar)
            {
                var first = source.FindIndex(x => !checkedBar.HasValue || x.Time > checkedBar.Value);
                if (first < 0 || source.Count < 20)
                    return;
                first = Math.Max(19, first);
                var features = FeatureEngine.CalculateNativeReversalHistory(source);
                var close = new List<decimal>(); var upper = new List<decimal>();
                var mid = new List<decimal>(); var lower = new List<decimal>();
                var rsi = new List<decimal>(); var line = new List<decimal>();
                var signal = new List<decimal>(); var histogram = new List<decimal>();
                void Add(List<decimal> row, decimal value)
                {
                    row.Add(value);
                    if (row.Count > 30) row.RemoveAt(0);
                }
                for (var i = Math.Max(0, first - 29); i < source.Count; i++)
                {
                    var f = features[i];
                    var isDaily = frame == Timeframe.D1;
                    Add(close, source[i].Close);
                    Add(upper, isDaily ? f.DailyBollingerUpperBand : f.H4BollingerUpperBand);
                    Add(mid, isDaily ? f.DailyBollingerMidBand : f.H4BollingerMidBand);
                    Add(lower, isDaily ? f.DailyBollingerLowerBand : f.H4BollingerLowerBand);
                    Add(rsi, isDaily ? f.DailyRSI14 : f.RSI14);
                    Add(line, isDaily ? f.DailyMACDLine : f.MACDLine);
                    Add(signal, isDaily ? f.DailyMACDSignal : f.MACDSignal);
                    Add(histogram, isDaily ? f.DailyMACDHistogram : f.MACDHistogram);
                    if (i < first) continue;
                    var found = BellPatternClassifier.TryFindReversalHookSignal(close, upper, mid, lower,
                        rsi, line, signal, histogram, out var age, out _);
                    if (found)
                    {
                        var bar = source[i - age];
                        var at = isDaily ? bar.Time.Date.AddHours(16) : bar.Time.AddHours(4);
                        events.Add((at, true, false, isDaily ? "D1" : "H4"));
                        if (i == source.Count - 1) recentSignal = true;
                    }
                    if (isDaily && mid[^1] > 0m)
                        events.Add((source[i].Time.Date.AddHours(16), false,
                            IsDeepBelowMid(source[i].Close, mid[^1]), "D1"));
                }
            }
            Read(daily, Timeframe.D1, state.LastDailyBar);
            Read(h4, Timeframe.H4, state.LastH4Bar);
            foreach (var e in events.OrderBy(x => x.At).ThenByDescending(x => x.Hook))
            {
                if (e.Hook)
                {
                    if (state.ObserveHook(e.At))
                        logger.Info($"Reversal watch hook observed: {ticker}, Timeframe={e.Frame}, ConfirmedAt={e.At:O}");
                }
                else state.ObservePosition(e.At, e.Deep);
            }
            if (daily.Count >= 20) state.LastDailyBar = daily[^1].Time;
            if (h4.Count >= 20) state.LastH4Bar = h4[^1].Time;
            if (hadItem && state.AwaitingNewDrop)
            {
                _items.Remove(ticker);
                // Let a newly observed recent hook pass through the normal publication
                // checks once, even though its watch cycle is already retired.
                if (recentSignal) _newSignals[ticker] = item!;
                logger.Info($"Reversal watch retired: {ticker}, HookConfirmedAt={state.LastHookAt:O}, Reason=hook-observed");
            }
        }

        public void MarkAttempt(string ticker, DateTime now)
        {
            if (!_items.TryGetValue(ticker, out var item))
                return;
            item.LastEvaluatedAt = now;
            item.LastStatus = "Watching";
            item.LastStatusReason = "Scan attempted; no published pattern result yet";
            item.LastStatusTime = now;
        }

        public void RecordResults(IEnumerable<CandidateDetails> candidates, DateTime now)
        {
            foreach (var candidate in candidates)
            {
                if (!_items.TryGetValue(candidate.Ticker, out var item))
                    continue;
                item.LastStatus = candidate.CandidateSource;
                item.LastStatusReason = candidate.PatternVerdictReason;
                item.LastStatusTime = now;
                item.Score = candidate.Score;
                item.Context = candidate.Context;
            }
        }

        public List<WishListItem> GetItems() => [.. _items.Values];
        public async Task SaveAsync()
        {
            if (!Enabled)
                return;
            var directory = Path.GetDirectoryName(StatePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporary = StatePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_states));
                File.Move(temporary, StatePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            await writer.WriteAsync(paths.GetWishListFile(), GetItems());
        }
    }
}
