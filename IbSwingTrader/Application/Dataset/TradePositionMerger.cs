namespace IbSwingTrader.Application.Dataset
{
    public class TradePositionMerger(
        IBuildDatasetSettingsProvider settingsProvider,
        ITextLogger logger) : ITradePositionMerger
    {
        private readonly IBuildDatasetSettingsProvider _settingsProvider = settingsProvider;
        private readonly ITextLogger _logger = logger;

        // Broker exports one row per matched entry/exit fill. A single real position can span
        // several rows when an order is split (e.g. a per-order share cap), or when it is closed
        // in several partial exits. This reconstructs one row per real trading decision by chaining
        // fills of the same ticker/direction that start close together in time, then drops whatever
        // is left over below the minimum size — those are probe/experiment orders, not real trades.
        public List<TradeRecord> Merge(List<TradeRecord> rawFills)
        {
            var settings = _settingsProvider.Get();
            var mergeWindow = TimeSpan.FromMinutes(settings.FillMergeWindowMinutes);

            var merged = new List<TradeRecord>();
            var droppedAsMicroFills = 0;

            var groups = rawFills
                .GroupBy(x => (x.Ticker, x.IsShort))
                .OrderBy(g => g.Key.Ticker)
                .ThenBy(g => g.Key.IsShort);

            foreach (var group in groups)
            {
                var fills = group.OrderBy(x => x.EntryTimeMarket).ToList();

                var batch = new List<TradeRecord> { fills[0] };

                for (int i = 1; i < fills.Count; i++)
                {
                    var fill = fills[i];
                    var previous = batch[^1];

                    if (fill.EntryTimeMarket - previous.EntryTimeMarket <= mergeWindow)
                    {
                        batch.Add(fill);
                        continue;
                    }

                    FlushBatch(batch, settings.MinimumEntryQuantity, merged, ref droppedAsMicroFills);
                    batch = [fill];
                }

                FlushBatch(batch, settings.MinimumEntryQuantity, merged, ref droppedAsMicroFills);
            }

            if (droppedAsMicroFills > 0)
            {
                _logger.Info(
                    $"TradePositionMerger: dropped {droppedAsMicroFills} merged trade(s) below minimum entry quantity ({settings.MinimumEntryQuantity})");
            }

            return merged
                .OrderBy(x => x.Ticker)
                .ThenBy(x => x.EntryTimeMarket)
                .ToList();
        }

        private static void FlushBatch(
            List<TradeRecord> batch,
            int minimumEntryQuantity,
            List<TradeRecord> merged,
            ref int droppedAsMicroFills)
        {
            var trade = MergeBatch(batch);

            if (trade.EntryQuantity < minimumEntryQuantity)
            {
                droppedAsMicroFills++;
                return;
            }

            merged.Add(trade);
        }

        private static TradeRecord MergeBatch(List<TradeRecord> batch)
        {
            if (batch.Count == 1)
                return batch[0];

            var totalEntryQuantity = batch.Sum(x => x.EntryQuantity);
            var totalExitQuantity = batch.Sum(x => x.ExitQuantity);

            var entryDollarWeight = batch.Sum(x => x.EntryPrice * x.EntryQuantity);
            var exitDollarWeight = batch.Sum(x => x.ExitPrice * x.ExitQuantity);

            var weightedEntryPrice = totalEntryQuantity > 0
                ? entryDollarWeight / totalEntryQuantity
                : batch[0].EntryPrice;

            var weightedExitPrice = totalExitQuantity > 0
                ? exitDollarWeight / totalExitQuantity
                : batch[0].ExitPrice;

            var weightedProfitPercent = entryDollarWeight > 0
                ? batch.Sum(x => x.ProfitPercent * x.EntryPrice * x.EntryQuantity) / entryDollarWeight
                : batch.Average(x => x.ProfitPercent);

            var entryTime = batch.Min(x => x.EntryTimeMarket);
            var exitTime = batch.Max(x => x.ExitTimeMarket);

            return new TradeRecord
            {
                Ticker = batch[0].Ticker,
                IsShort = batch[0].IsShort,

                EntryTimeMarket = entryTime,
                EntryPrice = weightedEntryPrice,
                EntryQuantity = totalEntryQuantity,

                ExitTimeMarket = exitTime,
                ExitPrice = weightedExitPrice,
                ExitQuantity = totalExitQuantity,

                ProfitPercent = weightedProfitPercent,
                HoldDays = (exitTime.Date - entryTime.Date).Days
            };
        }
    }
}
