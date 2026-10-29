namespace IbSwingTrader.Application.Candidates
{
    public static class ReversalDailyWeeklySupport
    {
        // Compare two observations of the SAME week. Older weeks are frozen;
        // only the latest completed native D1 close updates the current week.
        public static bool Allows(string origin, bool recognized, string phase,
            string weeklyRegime, IReadOnlyList<Candle> completedDaily,
            IReadOnlyList<FeatureSet> completedH4Features, decimal price,
            ReversalRecoveryMomentum momentum, DateTime decisionAt, out string reason)
        {
            bool Reject(string detail, out string result)
            {
                result = $"D1-led weekly recovery unavailable: {detail}";
                return false;
            }
            if (origin != "DeepDeclineRecovery" || !recognized || phase is not ("Active" or "MidCrossed"))
                return Reject("not-active-deep-decline-D1", out reason);
            if (weeklyRegime.Equals("Collapse", StringComparison.OrdinalIgnoreCase))
                return Reject("W1-Collapse", out reason);
            if (completedDaily.Count < 2 || completedDaily.Any(x => x.Close <= 0m ||
                    x.Time.Date.AddHours(16) > decisionAt) ||
                completedDaily.Zip(completedDaily.Skip(1), (a, b) => b.Time <= a.Time).Any(x => x))
                return Reject("missing-or-invalid-completed-native-D1", out reason);

            static DateTime Week(DateTime date) => date.Date.AddDays(-((int)date.DayOfWeek + 6) % 7);
            var latest = completedDaily[^1];
            var previous = completedDaily[^2];
            var week = Week(latest.Time);
            if (week != Week(decisionAt) || Week(previous.Time) != week)
                return Reject("need-two-completed-D1-observations-in-current-week", out reason);
            if (!momentum.IsImproving || completedH4Features.Count < 2 ||
                completedH4Features[^1].MACDHistogram <= completedH4Features[^2].MACDHistogram)
                return Reject("closed-and-fresh-H4-momentum-not-improving", out reason);

            var dailyFeatures = FeatureEngine.CalculateNativeReversalHistory(completedDaily.ToList());
            var dailyMid = dailyFeatures[^1].DailyBollingerMidBand;
            if (price <= 0m || dailyMid <= 0m || (dailyMid / price - 1m) * 100m < 2m ||
                dailyFeatures[^1].DailyMACDHistogram <= dailyFeatures[^2].DailyMACDHistogram)
                return Reject("D1-momentum-or-room-to-mid-insufficient", out reason);

            var weekly = completedDaily.GroupBy(x => Week(x.Time))
                .Select(g => new Candle { Time = g.Key, Close = g.Last().Close }).ToList();
            if (weekly.Count < 26)
                return Reject("insufficient-native-weekly-history", out reason);
            var current = FeatureEngine.CalculateNativeReversalHistory(weekly)[^1];
            weekly[^1] = new Candle { Time = week, Close = previous.Close };
            var priorObservation = FeatureEngine.CalculateNativeReversalHistory(weekly)[^1];
            if (current.DailyMACDLine <= priorObservation.DailyMACDLine ||
                current.DailyMACDHistogram <= priorObservation.DailyMACDHistogram)
                return Reject("current-week-MACD-not-improving", out reason);

            reason = FormattableString.Invariant(
                $"D1-led recovery with improving current-week MACD: Origin={origin}, Source=CompletedNativeD1, WeekStart={week:yyyy-MM-dd}, PreviousObservation={previous.Time.Date.AddHours(16):O}, LatestObservation={latest.Time.Date.AddHours(16):O}, DecisionAt={decisionAt:O}, PreviousWeeklyLine={priorObservation.DailyMACDLine}, WeeklyLine={current.DailyMACDLine}, PreviousWeeklyHistogram={priorObservation.DailyMACDHistogram}, WeeklyHistogram={current.DailyMACDHistogram}, DailyHistogram={dailyFeatures[^1].DailyMACDHistogram}, RoomToD1MidPct={(dailyMid / price - 1m) * 100m}; {momentum.Diagnostics}");
            return true;
        }
    }
}
