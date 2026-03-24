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

            // -------------------------
            // Daily score
            // -------------------------
            dailyScore += Clamp(-f.DistanceTo20dHigh, 0m, 25m) * 1.2m;
            dailyScore += Clamp(-f.DistanceTo52wHigh, 0m, 40m) * 0.8m;
            dailyScore += Clamp(-f.DailyMaSignedDistancePct, 0m, 10m) * 1.5m;

            dailyScore += ScoreRsiZone(f.DailyRSI14, 35m, 50m, 55m);
            dailyScore += ScoreNegativeButNotBroken(f.DailyMACDLineMinusSignal, -3m, 0m);

            if (f.DailyRSI14 < 30m)
                dailyScore -= 20m;

            // -------------------------
            // Weekly score
            // -------------------------

            // 1. Price below weekly MA is good for wish list,
            // but too deep weakness should not get unlimited bonus.
            if (f.WeeklyMaSignedDistancePct.HasValue)
            {
                var weeklyMa = f.WeeklyMaSignedDistancePct.Value;

                weeklyScore += Clamp(-weeklyMa, 0m, 18m) * 1.2m;

                if (weeklyMa < -20m)
                    weeklyScore -= 12m;
            }

            // 2. Weekly RSI should be weak enough for pullback,
            // but not completely broken.
            if (f.WeeklyRSI14.HasValue)
            {
                weeklyScore += ScoreRsiZone(f.WeeklyRSI14.Value, 32m, 43m, 55m);

                if (f.WeeklyRSI14.Value < 28m)
                    weeklyScore -= 12m;
            }

            // 3. Weekly MACD is better when still negative / near zero,
            // but not deeply broken.
            if (f.WeeklyMACDLineMinusSignal.HasValue)
            {
                weeklyScore += ScoreNegativeButNotBroken(
                    f.WeeklyMACDLineMinusSignal.Value,
                    -4m,
                    0.8m);

                if (f.WeeklyMACDLineMinusSignal.Value < -4m)
                    weeklyScore -= 10m;
            }

            // 4. Weekly improvement matters.
            // We want to see that the weekly picture is not just weak,
            // but starting to improve.
            if (f.WeeklyMaSignedDistancePct.HasValue && snapshot.Prev3.WeeklyMaSignedDistancePct.HasValue)
            {
                var weeklyMaDelta3 = f.WeeklyMaSignedDistancePct.Value - snapshot.Prev3.WeeklyMaSignedDistancePct.Value;

                // Less negative / more positive = improvement.
                if (weeklyMaDelta3 > 0m)
                    weeklyScore += Clamp(weeklyMaDelta3, 0m, 6m) * 1.8m;
            }

            if (f.WeeklyMACDLineMinusSignal.HasValue && snapshot.Prev3.WeeklyMACDLineMinusSignal.HasValue)
            {
                var weeklyMacdDelta3 = f.WeeklyMACDLineMinusSignal.Value - snapshot.Prev3.WeeklyMACDLineMinusSignal.Value;

                if (weeklyMacdDelta3 > 0m)
                    weeklyScore += Clamp(weeklyMacdDelta3, 0m, 4m) * 2.5m;
            }

            if (f.WeeklyRSI14.HasValue && snapshot.Prev3.WeeklyRSI14.HasValue)
            {
                var weeklyRsiDelta3 = f.WeeklyRSI14.Value - snapshot.Prev3.WeeklyRSI14.Value;

                if (weeklyRsiDelta3 > 0m)
                    weeklyScore += Clamp(weeklyRsiDelta3, 0m, 8m) * 1.2m;
            }

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