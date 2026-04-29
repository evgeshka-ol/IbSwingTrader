
namespace IbSwingTrader.Application.Candidates
{
    public class CandidateScore(
        IGetCandidatesSettingsProvider settingsProvider) : ICandidateScore
    {
        private readonly IGetCandidatesSettingsProvider _settingsProvider = settingsProvider;

        public decimal Calculate(CandidateSignalSnapshot snapshot)
        {
            var f = snapshot.Current;
            var s = _settingsProvider.Get().EntryFilter;
            decimal score = 0m;

            score += Positive(snapshot.DailyMaDelta3) * 2.0m;
            score += Positive(snapshot.DailyRsiDelta3) * 2.0m;
            score += Positive(snapshot.DailyMacdDelta3) * 2.0m;

            score += Positive(snapshot.H4MaDelta3) * 1.2m;
            score += Positive(snapshot.H4RsiDelta3) * 1.0m;
            score += Positive(snapshot.H4MacdDelta3) * 1.2m;

            if (f.DailyMaSignedDistancePct > 0m)
                score += 12m;

            if (f.DailyMaSignedDistancePct > 5m)
                score += 8m;

            if (f.DailyRSI14 >= 40m && f.DailyRSI14 <= 58m)
                score += 10m;

            if (f.DailyMACDLineMinusSignal > snapshot.Prev1.DailyMACDLineMinusSignal)
                score += 8m;

            if (snapshot.DailyRsiDelta3 >= s.MinDailyRsiDelta3Strong)
                score += 10m;

            if (snapshot.DailyMaDelta3 > s.MinDailyMaDelta3 &&
                snapshot.DailyRsiDelta3 > s.MinDailyRsiDelta3 &&
                f.DailyMaSignedDistancePct > 0m)
            {
                score += 12m;
            }

            // Research winners suggest a second archetype besides pure momentum:
            // deep pullback / recovery names that are still below recent highs but
            // already show improving daily structure. Keep this as a soft bonus.
            if (f.DistanceTo20dHigh <= -20m &&
                snapshot.DailyMaDelta3 > 0m &&
                snapshot.DailyRsiDelta3 > 0m)
            {
                score += 6m;

                if (f.DailyRSI14 >= 35m && f.DailyRSI14 <= 52m)
                    score += 4m;

                if (!f.WeeklyMACDLineMinusSignal.HasValue || f.WeeklyMACDLineMinusSignal.Value <= 0.5m)
                    score += 4m;
            }

            if (f.DistanceTo20dHigh <= -12m &&
                f.DailyBollingerBandWidthPct >= 45m &&
                snapshot.DailyMaDelta3 > 0m &&
                snapshot.DailyRsiDelta3 > 0m)
            {
                score += 10m;

                if ((f.WeeklyBollingerBandWidthPct ?? 0m) >= 70m)
                    score += 6m;
            }

            if (f.DailyBollingerBandWidthPct >= 70m &&
                f.DailyRSI14 >= 60m &&
                f.DailyMACDLineMinusSignal > 0m &&
                snapshot.DailyMaDelta3 > 0m)
            {
                score += 12m;
            }

            if (f.WeeklyMACDLineMinusSignal.HasValue &&
                f.WeeklyMACDLineMinusSignal.Value > 0.5m)
            {
                score -= 12m;
            }

            if (f.DailyRSI14 > 72m)
                score -= 14m;
            else if (f.DailyRSI14 > 65m)
                score -= 8m;

            if (f.DistanceTo20dHigh > -2m)
                score -= 14m;
            else if (f.DistanceTo20dHigh > -5m)
                score -= 6m;

            if (f.DistanceTo20dHigh > -3m)
                score -= 10m;

            return score;
        }

        private static decimal Positive(decimal value)
            => value > 0m ? value : 0m;
    }
}
