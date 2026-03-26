using IbSwingTrader.Interfaces;
using IbSwingTrader.Models.Tickers;

namespace IbSwingTrader.Services.WishListFiltering
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
                x => x,
                StringComparer.OrdinalIgnoreCase);

            foreach (var newItem in newItems)
            {
                if (result.TryGetValue(newItem.Ticker, out var existing))
                {
                    var firstSeen = existing.FirstSeenMarketTime
                        ?? existing.Scan?.ScanTimeMarket
                        ?? newItem.FirstSeenMarketTime
                        ?? newItem.Scan.ScanTimeMarket;

                    existing.Scan = newItem.Scan;
                    existing.Score = newItem.Score;
                    existing.Context = newItem.Context;

                    existing.FirstSeenMarketTime = firstSeen;
                    existing.LastEvaluatedMarketTime =
                        newItem.LastEvaluatedMarketTime
                        ?? (DateTime?)newItem.Scan.ScanTimeMarket
                        ?? existing.LastEvaluatedMarketTime;

                    existing.ExpectedBarsToTarget =
                        newItem.ExpectedBarsToTarget
                        ?? existing.ExpectedBarsToTarget;

                    existing.ExpectedTargetMarketTime =
                        newItem.ExpectedTargetMarketTime
                        ?? existing.ExpectedTargetMarketTime;
                }
                else
                {
                    newItem.FirstSeenMarketTime ??= newItem.Scan.ScanTimeMarket;
                    newItem.LastEvaluatedMarketTime ??= newItem.Scan.ScanTimeMarket;
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
    }
}