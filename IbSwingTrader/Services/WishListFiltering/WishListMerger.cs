using IbSwingTrader.Interfaces;
using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Infrastructure.Persistence
{
    public class WishListMerger : IWishListMerger
    {
        public List<WishListItem> Merge(
            List<WishListItem> currentItems,
            List<WishListItem> newItems,
            DateTime marketNow)
        {
            ArgumentNullException.ThrowIfNull(currentItems);
            ArgumentNullException.ThrowIfNull(newItems);

            var currentMap = currentItems.ToDictionary(
                x => x.Ticker,
                x => x,
                StringComparer.OrdinalIgnoreCase);

            var result = new List<WishListItem>(newItems.Count);

            foreach (var newItem in newItems)
            {
                if (currentMap.TryGetValue(newItem.Ticker, out var existing))
                {
                    newItem.FirstSeenMarketTime =
                        existing.FirstSeenMarketTime
                        ?? existing.Scan?.ScanTimeMarket
                        ?? marketNow;

                    newItem.LastEvaluatedMarketTime = marketNow;

                    newItem.ExpectedBarsToTarget ??= existing.ExpectedBarsToTarget;
                    newItem.ExpectedTargetMarketTime ??= existing.ExpectedTargetMarketTime;
                }
                else
                {
                    newItem.FirstSeenMarketTime = marketNow;
                    newItem.LastEvaluatedMarketTime = marketNow;
                }

                result.Add(newItem);
            }

            return
            [
                .. result
                    .OrderByDescending(x => x.Score.Score)
                    .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase)
            ];
        }
    }
}