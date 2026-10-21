namespace IbSwingTrader.Application.Candidates
{
    public static class ReversalH4DailySupport
    {
        // Alternative readiness for an already recognized H4 episode, not a pattern detector.
        public static bool Allows(bool recognized, string phase, string dailyRegime,
            decimal price, IReadOnlyList<decimal> dailyMid,
            IReadOnlyList<decimal> h4Mid, IReadOnlyList<decimal> h4Lower,
            IReadOnlyList<decimal> h4Histogram, ReversalRecoveryMomentum momentum,
            out string reason)
        {
            bool Reject(string detail, out string result)
            {
                result = $"H4-led recovery unavailable: {detail}";
                return false;
            }
            if (!recognized || phase is not ("Active" or "MidCrossed"))
                return Reject("episode-not-active", out reason);
            if (dailyRegime.Equals("Collapse", StringComparison.OrdinalIgnoreCase))
                return Reject("D1-Collapse", out reason);
            if (price <= 0m || dailyMid.Count < 1 || dailyMid[^1] <= 0m ||
                h4Mid.Count < 3 || h4Lower.Count < 2 || h4Histogram.Count < 2 ||
                h4Mid.TakeLast(3).Any(x => x <= 0m) || h4Lower.TakeLast(2).Any(x => x <= 0m))
                return Reject("missing-or-invalid-series", out reason);
            var room = (dailyMid[^1] / price - 1m) * 100m;
            if (price >= dailyMid[^1] || room < 2m)
                return Reject("less-than-2pct-room-to-D1-mid", out reason);
            if (!momentum.IsImproving)
                return Reject("fresh-H4-momentum-not-improving", out reason);
            var latestMidDelta = h4Mid[^1] - h4Mid[^2];
            var previousMidDelta = h4Mid[^2] - h4Mid[^3];
            if (h4Lower[^1] <= h4Lower[^2] ||
                (latestMidDelta < 0m && latestMidDelta <= previousMidDelta))
                return Reject("H4-bands-not-turning-up", out reason);
            if (h4Histogram[^1] <= h4Histogram[^2])
                return Reject("closed-H4-momentum-not-improving", out reason);
            reason = FormattableString.Invariant(
                $"H4-led recovery below D1 mid: RoomToMidPct={room:0.##}, H4MidDelta={latestMidDelta}, H4PreviousMidDelta={previousMidDelta}, H4LowerDelta={h4Lower[^1] - h4Lower[^2]}, H4Histogram={h4Histogram[^1]}; {momentum.Diagnostics}");
            return true;
        }
    }
}
