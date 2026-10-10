namespace IbSwingTrader.Application.WishList
{
    // Persistent recall universe. Enrollment is independent of pattern/entry readiness.
    public sealed class ReversalWatchList(
        IWishListReader reader, IWishListResultWriter writer, IAgentPathService paths,
        IGetCandidatesSettingsProvider settingsProvider, IMarketSettingsProvider marketSettings,
        ITextLogger logger)
    {
        private readonly Dictionary<string, WishListItem> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _atStart = new(StringComparer.OrdinalIgnoreCase);
        private List<WishListItem> _unmanaged = [];
        private ReversalWatchListSettings Settings => settingsProvider.Get().ReversalWatchList;
        public bool Enabled => Settings.Enabled;

        public async Task LoadAsync(DateTime now)
        {
            _items.Clear();
            _atStart.Clear();
            _unmanaged = [];
            if (!Enabled)
                return;
            var loaded = await reader.ReadAsync(paths.GetWishListFile());
            foreach (var item in loaded.Where(x => !string.IsNullOrWhiteSpace(x.Ticker)))
            {
                if (!item.LastDropSeenAt.HasValue && !IsDropCode(item.Scan.PresetScanCode))
                {
                    _unmanaged.Add(item);
                    continue;
                }
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

        public void ObserveDrop(StockInfo stock, PresetScanCode preset, DateTime now)
        {
            if (!CapturesCode(preset.ScanCode))
                return;
            if (!_items.TryGetValue(stock.Ticker, out var item))
            {
                item = new WishListItem
                {
                    Ticker = stock.Ticker, FirstSeen = now,
                    Scan = new ScanInfo(), Score = new ScoreInfo(), Context = new MarketContextInfo(),
                    LastStatus = "Watching", LastStatusReason = "New loser; waiting for a confirmed pattern", LastStatusTime = now
                };
                _items[stock.Ticker] = item;
                logger.Info($"Reversal watch enrolled: {stock.Ticker}, Preset={preset.ScanCode}");
            }
            item.FirstSeen ??= now;
            item.LastDropSeenAt = now;
            item.WatchCurrency = stock.Currency;
            item.WatchStockType = stock.StockType ?? string.Empty;
            item.Scan = new ScanInfo
            {
                PresetScanCode = preset.ScanCode, PresetDescription = preset.Description,
                ScanTime = now, ScanTimeZone = marketSettings.Get().Timezone
            };
            item.ExpectedTargetTime = null;
            item.ExpectedBarsToTarget = null;
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

        public List<WishListItem> GetItems() => [.. _unmanaged, .. _items.Values];
        public async Task SaveAsync()
        {
            if (Enabled)
                await writer.WriteAsync(paths.GetWishListFile(), GetItems());
        }
    }
}
