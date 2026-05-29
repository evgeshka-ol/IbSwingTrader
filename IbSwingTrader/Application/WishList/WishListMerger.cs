
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
                    CopySeries(existing, newItem);

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

        private static void CopySeries(WishListItem target, WishListItem source)
        {
            target.RecentDailyBbUpperBandSeries = [.. source.RecentDailyBbUpperBandSeries];
            target.RecentDailyBbMidBandSeries = [.. source.RecentDailyBbMidBandSeries];
            target.RecentDailyBbLowerBandSeries = [.. source.RecentDailyBbLowerBandSeries];
            target.RecentDailyMacdLineSeries = [.. source.RecentDailyMacdLineSeries];
            target.RecentDailyMacdSignalSeries = [.. source.RecentDailyMacdSignalSeries];
            target.RecentDailyMacdHistogramSeries = [.. source.RecentDailyMacdHistogramSeries];
            target.RecentDailyRsiSeries = [.. source.RecentDailyRsiSeries];
            target.RecentWeeklyBbUpperBandSeries = [.. source.RecentWeeklyBbUpperBandSeries];
            target.RecentWeeklyBbMidBandSeries = [.. source.RecentWeeklyBbMidBandSeries];
            target.RecentWeeklyBbLowerBandSeries = [.. source.RecentWeeklyBbLowerBandSeries];
            target.RecentWeeklyMacdLineSeries = [.. source.RecentWeeklyMacdLineSeries];
            target.RecentWeeklyMacdSignalSeries = [.. source.RecentWeeklyMacdSignalSeries];
            target.RecentWeeklyMacdHistogramSeries = [.. source.RecentWeeklyMacdHistogramSeries];
            target.RecentWeeklyRsiSeries = [.. source.RecentWeeklyRsiSeries];
            target.RecentH4BbUpperBandSeries = [.. source.RecentH4BbUpperBandSeries];
            target.RecentH4BbMidBandSeries = [.. source.RecentH4BbMidBandSeries];
            target.RecentH4BbLowerBandSeries = [.. source.RecentH4BbLowerBandSeries];
            target.RecentH4MacdLineSeries = [.. source.RecentH4MacdLineSeries];
            target.RecentH4MacdSignalSeries = [.. source.RecentH4MacdSignalSeries];
            target.RecentH4MacdHistogramSeries = [.. source.RecentH4MacdHistogramSeries];
            target.RecentH4RsiSeries = [.. source.RecentH4RsiSeries];
        }
    }
}
