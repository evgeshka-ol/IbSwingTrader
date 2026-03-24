using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.WishListFiltering
{
    public class WishListScore : IWishListScore
    {
        public WishListScoreResult Calculate(CandidateSignalSnapshot snapshot)
        {
            var f = snapshot.Current;

            decimal dailyScore = 0m;
            decimal weeklyScore = 0m;

            dailyScore += Clamp(-f.DistanceTo20dHigh, 0m, 25m) * 1.2m;
            dailyScore += Clamp(-f.DistanceTo52wHigh, 0m, 40m) * 0.8m;
            dailyScore += Clamp(-f.DailyMaSignedDistancePct, 0m, 10m) * 1.5m;

            dailyScore += ScoreRsiZone(f.DailyRSI14, 35m, 50m, 55m);
            dailyScore += ScoreNegativeButNotBroken(f.DailyMACDLineMinusSignal, -3m, 0m);

            if (f.DailyRSI14 < 30m)
                dailyScore -= 20m;

            if (f.WeeklyMaSignedDistancePct.HasValue)
                weeklyScore += Clamp(-f.WeeklyMaSignedDistancePct.Value, 0m, 12m) * 1.0m;

            if (f.WeeklyMaSignedDistancePct.HasValue && f.WeeklyMaSignedDistancePct.Value < -12m)
                weeklyScore -= 20m;

            return new WishListScoreResult
            {
                DailyScore = dailyScore,
                WeeklyScore = weeklyScore,
                TotalScore = dailyScore + weeklyScore
            };
        }

        private static decimal Clamp(decimal value, decimal min, decimal max)
            => Math.Min(max, Math.Max(min, value));

        private static decimal ScoreRsiZone(decimal value, decimal left, decimal center, decimal right)
        {
            if (value < left || value > right)
                return 0m;

            if (value <= center)
                return (value - left) / (center - left) * 15m;

            return (right - value) / (right - center) * 15m;
        }

        private static decimal ScoreNegativeButNotBroken(decimal value, decimal min, decimal max)
        {
            if (value < min || value > max)
                return 0m;

            return (value - min) / (max - min) * 10m;
        }
    }
}