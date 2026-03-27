
namespace IbSwingTrader.Application.WishList
{
    public class WishListMerger : IWishListMerger
    {
        public List<WishListItem> Merge(
            List<WishListItem> currentItems,
            List<WishListItem> newItems)
        {
            ArgumentNullException.ThrowIfNull(currentItems);
            ArgumentNullException.ThrowIfNull(newItems);

            var result = currentItems.ToDictionary(
                x => x.Ticker,
                x => NormalizeExistingItem(x),
                StringComparer.OrdinalIgnoreCase);

            foreach (var newItem in newItems)
            {
                if (result.TryGetValue(newItem.Ticker, out var existing))
                {
                    var restoredFirstSeen =
                        existing.FirstSeenMarketTime
                        ?? existing.Scan?.ScanTimeMarket
                        ?? newItem.Scan?.ScanTimeMarket;

                    var currentRunMarketTime =
                        newItem.LastEvaluatedMarketTime
                        ?? newItem.Scan?.ScanTimeMarket;

                    existing.Scan = newItem.Scan!;
                    existing.Score = newItem.Score!;
                    existing.Context = newItem.Context!;

                    existing.FirstSeenMarketTime = restoredFirstSeen;
                    existing.LastEvaluatedMarketTime =
                        currentRunMarketTime
                        ?? existing.LastEvaluatedMarketTime
                        ?? existing.Scan?.ScanTimeMarket;

                    existing.ExpectedBarsToTarget =
                        newItem.ExpectedBarsToTarget
                        ?? existing.ExpectedBarsToTarget;

                    existing.ExpectedTargetMarketTime =
                        newItem.ExpectedTargetMarketTime
                        ?? existing.ExpectedTargetMarketTime;
                }
                else
                {
                    newItem.FirstSeenMarketTime ??=
                        newItem.Scan?.ScanTimeMarket;

                    newItem.LastEvaluatedMarketTime ??=
                        newItem.Scan?.ScanTimeMarket;

                    result[newItem.Ticker] = newItem;
                }
            }

            return
            [
                .. result.Values
                    .OrderByDescending(x => x.Score.Score)
                    .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
            ];
        }

        private static WishListItem NormalizeExistingItem(WishListItem item)
        {
            item.FirstSeenMarketTime ??=
                item.Scan?.ScanTimeMarket;

            item.LastEvaluatedMarketTime ??=
                item.Scan?.ScanTimeMarket;

            return item;
        }
    }
}