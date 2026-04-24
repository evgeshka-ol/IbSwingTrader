
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
                        existing.FirstSeen
                        ?? existing.Scan?.ScanTime
                        ?? newItem.Scan?.ScanTime;

                    var currentRunMarketTime =
                        newItem.LastEvaluatedAt
                        ?? newItem.Scan?.ScanTime;

                    existing.Scan = newItem.Scan!;
                    existing.Score = newItem.Score!;
                    existing.Context = newItem.Context!;

                    existing.FirstSeen = restoredFirstSeen;
                    existing.LastEvaluatedAt =
                        currentRunMarketTime
                        ?? existing.LastEvaluatedAt
                        ?? existing.Scan?.ScanTime;

                    existing.ExpectedBarsToTarget =
                        newItem.ExpectedBarsToTarget
                        ?? existing.ExpectedBarsToTarget;

                    existing.ExpectedTargetTime =
                        newItem.ExpectedTargetTime
                        ?? existing.ExpectedTargetTime;
                }
                else
                {
                    newItem.FirstSeen ??=
                        newItem.Scan?.ScanTime;

                    newItem.LastEvaluatedAt ??=
                        newItem.Scan?.ScanTime;

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
            item.FirstSeen ??=
                item.Scan?.ScanTime;

            item.LastEvaluatedAt ??=
                item.Scan?.ScanTime;

            return item;
        }
    }
}
