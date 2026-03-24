using IbSwingTrader.Interfaces;
using IbSwingTrader.Models;

namespace IbSwingTrader.Services.WishListFiltering
{
    public class WishListScore : IWishListScore
    {
        public decimal Calculate(CandidateSignalSnapshot snapshot)
        {
            var f = snapshot.Current;
            decimal score = 0m;

            score += Clamp(-f.DistanceTo20dHigh, 0m, 25m) * 1.2m;
            score += Clamp(-f.DistanceTo52wHigh, 0m, 40m) * 0.8m;
            score += Clamp(-f.DailyMaSignedDistancePct, 0m, 10m) * 1.5m;

            if (f.WeeklyMaSignedDistancePct.HasValue)
                score += Clamp(-f.WeeklyMaSignedDistancePct.Value, 0m, 12m) * 1.0m;

            score += ScoreRsiZone(f.DailyRSI14, 35m, 50m, 55m);
            score += ScoreNegativeButNotBroken(f.DailyMACDLineMinusSignal, -3m, 0m);

            if (f.DailyRSI14 < 30m)
                score -= 20m;

            if (f.WeeklyMaSignedDistancePct.HasValue && f.WeeklyMaSignedDistancePct.Value < -12m)
                score -= 20m;

            return score;
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
