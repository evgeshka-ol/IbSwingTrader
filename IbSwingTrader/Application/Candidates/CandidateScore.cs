
namespace IbSwingTrader.Application.Candidates
{
    public class CandidateScore : ICandidateScore
    {
        public decimal Calculate(CandidateSignalSnapshot snapshot)
        {
            var f = snapshot.Current;
            decimal score = 0m;

            score += Positive(snapshot.DailyMaDelta3) * 2.0m;
            score += Positive(snapshot.DailyRsiDelta3) * 1.5m;
            score += Positive(snapshot.DailyMacdDelta3) * 2.0m;

            score += Positive(snapshot.H4MaDelta3) * 1.2m;
            score += Positive(snapshot.H4RsiDelta3) * 1.0m;
            score += Positive(snapshot.H4MacdDelta3) * 1.2m;

            if (f.DailyRSI14 >= 40m && f.DailyRSI14 <= 58m)
                score += 10m;

            if (f.DailyMACDLineMinusSignal > snapshot.Prev1.DailyMACDLineMinusSignal)
                score += 8m;

            if (f.DistanceTo20dHigh > -3m)
                score -= 10m;

            return score;
        }

        private static decimal Positive(decimal value)
            => value > 0m ? value : 0m;
    }
}