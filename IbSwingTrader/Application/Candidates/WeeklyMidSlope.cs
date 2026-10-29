namespace IbSwingTrader.Application.Candidates
{
    public readonly record struct WeeklyMidSlopeResult(bool Available, bool IsGentleDecline,
        decimal RecentSlopePctPerWeek, decimal WorstStepPct, string Diagnostics);

    public static class WeeklyMidSlope
    {
        public const decimal MaximumDeclinePctPerWeek = 1.5m;

        public static WeeklyMidSlopeResult Analyze(IReadOnlyList<decimal> mid)
        {
            if (mid.Count < 3 || mid.TakeLast(3).Any(x => x <= 0m))
                return new(false, false, 0m, 0m, "WeeklyMidSlope=Unavailable");
            var first = mid[^3];
            var ratio = mid[^1] / first;
            var root = ratio >= 1m ? ratio : 1m;
            for (var i = 0; i < 100; i++)
            {
                var next = (root + ratio / root) / 2m;
                if (Math.Abs(next - root) <= 0.000000000000000000000001m)
                {
                    root = next;
                    break;
                }
                root = next;
            }
            var slope = (root - 1m) * 100m;
            var step1 = (mid[^2] / first - 1m) * 100m;
            var step2 = (mid[^1] / mid[^2] - 1m) * 100m;
            var worst = Math.Min(step1, step2);
            var floor = 1m - MaximumDeclinePctPerWeek / 100m;
            // Compare the squared boundary directly, without rounded/double sqrt decisions.
            var gentle = ratio <= 1m && ratio >= floor * floor && worst >= -MaximumDeclinePctPerWeek;
            return new(true, gentle, slope, worst, FormattableString.Invariant(
                $"WeeklyMidSlope(GentleDecline={gentle}, RecentPctPerWeek={slope}, Step1Pct={step1}, Step2Pct={step2}, WorstStepPct={worst}, MaximumDeclinePctPerWeek={MaximumDeclinePctPerWeek}, MidTail=[{first} {mid[^2]} {mid[^1]}])"));
        }
    }
}
