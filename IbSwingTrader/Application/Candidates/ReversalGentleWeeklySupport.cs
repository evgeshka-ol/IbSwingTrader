namespace IbSwingTrader.Application.Candidates
{
    // Publication-only support. Recognition and the saved local phase are never rewritten.
    public static class ReversalGentleWeeklySupport
    {
        public const decimal MinimumRoomPct = 2m;

        public static bool Allows(string timeframe, string origin, bool recognized, string phase,
            string weeklyRegime, IReadOnlyList<decimal> weeklyMid,
            IReadOnlyList<Candle> completedDaily, DateTime expectedClosedDailyDate,
            IReadOnlyList<FeatureSet> completedH4Features, decimal price,
            ReversalRecoveryMomentum momentum, DateTime decisionAt, out string reason)
        {
            var slope = WeeklyMidSlope.Analyze(weeklyMid);
            bool Reject(string detail, out string result)
            {
                result = $"Gentle-W1 support unavailable: {detail}; {slope.Diagnostics}";
                return false;
            }
            if (timeframe is not ("D1" or "Daily") || origin != "DeepDeclineRecovery" || !recognized ||
                phase is not ("Active" or "MidCrossed" or "Stalled"))
                return Reject("not-recognized-deep-decline-D1", out reason);
            if (weeklyRegime.Equals("Collapse", StringComparison.OrdinalIgnoreCase))
                return Reject("W1-Collapse", out reason);
            if (!slope.Available || !slope.IsGentleDecline)
                return Reject("weekly-mid-not-gentle", out reason);
            // Availability is typed evidence from ReversalH4Momentum's session/quote/base guards.
            // Weak but valid fresh H4 is allowed only by the anticipatory Stalled branch.
            if (!momentum.IsAvailable || price <= 0m)
                return Reject("fresh-H4-observation-unavailable", out reason);
            if (completedDaily.Count < 21 || completedDaily.Any(x => x.Close <= 0m ||
                    x.Time.Date.AddHours(16) > decisionAt) ||
                completedDaily.Zip(completedDaily.Skip(1), (a, b) => b.Time <= a.Time).Any(x => x) ||
                expectedClosedDailyDate.Date.AddHours(16) > decisionAt ||
                completedDaily[^1].Time.Date != expectedClosedDailyDate.Date)
                return Reject("missing-stale-or-invalid-completed-native-D1", out reason);
            var daily = FeatureEngine.CalculateNativeReversalHistory(completedDaily.ToList());
            if (daily[^1].DailyMACDHistogram <= daily[^2].DailyMACDHistogram)
                return Reject("closed-D1-momentum-not-improving", out reason);
            var weeklyRoom = (weeklyMid[^1] / price - 1m) * 100m;
            if (weeklyRoom < MinimumRoomPct)
                return Reject("insufficient-room-to-W1-mid", out reason);

            var anticipatory = phase == "Stalled";
            var dailyRoom = (daily[^1].DailyBollingerMidBand / price - 1m) * 100m;
            if (anticipatory)
            {
                if (dailyRoom < MinimumRoomPct)
                    return Reject("anticipatory-room-to-D1-mid-insufficient", out reason);
                var last = completedDaily[^1];
                if (last.Open <= 0m || last.Low <= 0m || last.High < Math.Max(last.Open, last.Close) ||
                    last.Low > Math.Min(last.Open, last.Close) || price < last.Low)
                    return Reject("anticipatory-price-below-last-D1-low-or-invalid-range", out reason);
            }
            else if (!momentum.IsImproving || completedH4Features.Count < 2 ||
                     completedH4Features[^1].MACDHistogram <= completedH4Features[^2].MACDHistogram)
                return Reject("closed-and-fresh-H4-momentum-not-improving", out reason);

            reason = FormattableString.Invariant(
                $"Gentle-W1 support: Admission={(anticipatory ? "AnticipatoryD1Recovery" : "ActiveD1Recovery")}, LocalPhase={phase}, Origin={origin}, DecisionAt={decisionAt:O}, LastClosedD1={completedDaily[^1].Time.Date.AddHours(16):O}, PreviousDailyHistogram={daily[^2].DailyMACDHistogram}, DailyHistogram={daily[^1].DailyMACDHistogram}, LastDailyLow={completedDaily[^1].Low}, Price={price}, RoomToW1MidPct={weeklyRoom}, RoomToD1MidPct={dailyRoom}; {slope.Diagnostics}; {momentum.Diagnostics}");
            return true;
        }
    }
}
