
namespace IbSwingTrader.Application.WishList
{
    public class WishListScore : IWishListScore
    {
        public WishListScoreResult Calculate(CandidateSignalSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            var dailyDistance = snapshot.Current.DailyMaSignedDistancePct;
            var weeklyDistance = snapshot.Current.WeeklyMaSignedDistancePct;

            var dailyScore = CalculateDistanceScore(dailyDistance, 10m);
            var weeklyScore = CalculateDistanceScore(weeklyDistance, 15m);

            var totalScore = dailyScore + weeklyScore;

            return new WishListScoreResult
            {
                DailyScore = dailyScore,
                WeeklyScore = weeklyScore,
                TotalScore = totalScore
            };
        }

        private static decimal CalculateDistanceScore(decimal? signedDistancePct, decimal maxDepthPct)
        {
            if (!signedDistancePct.HasValue)
                return 0m;

            if (signedDistancePct.Value >= 0m)
                return 0m;

            var depthBelowMid = Math.Abs(signedDistancePct.Value);
            var cappedDepth = Math.Min(depthBelowMid, maxDepthPct);

            return Math.Round(cappedDepth, 2, MidpointRounding.AwayFromZero);
        }
    }
}