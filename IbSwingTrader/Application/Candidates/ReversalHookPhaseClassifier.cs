namespace IbSwingTrader.Application.Candidates
{
    // Alternative recognition only. These phases do not authorize an entry or rank a ticker.
    public readonly record struct ReversalHookPhase(string Name, int BarsSinceBend, string Diagnostics);

    public static class ReversalHookPhaseClassifier
    {
        public static bool TryGetSavedPattern(string reason, out string pattern)
        {
            foreach (var phase in new[] { "Preparing", "Active", "TargetReached", "Stalled" })
            {
                pattern = $"ReversalHook{phase}";
                if (reason.StartsWith($"{pattern} detected on ", StringComparison.Ordinal) &&
                    reason.Contains("; DiagnosticOnly=True;", StringComparison.Ordinal))
                    return true;
            }
            pattern = string.Empty;
            return false;
        }

        public static ReversalHookPhase Analyze(
            IReadOnlyList<decimal> close,
            IReadOnlyList<decimal> mid,
            IReadOnlyList<decimal> histogram,
            decimal snapshotPrice)
        {
            var count = Math.Min(close.Count, Math.Min(mid.Count, histogram.Count));
            if (count < 7)
                return new("Unavailable", -1, "insufficient completed bars");

            // Align tails, since indicator warmup can produce different series lengths.
            var prices = close.TakeLast(count).ToArray();
            var bands = mid.TakeLast(count).ToArray();
            var momentum = histogram.TakeLast(count).ToArray();
            var bend = -1;
            for (var i = Math.Max(4, count - 6); i < count; i++)
            {
                var d1 = bands[i - 3] - bands[i - 4];
                var d2 = bands[i - 2] - bands[i - 3];
                var d3 = bands[i - 1] - bands[i - 2];
                var latest = bands[i] - bands[i - 1];
                var prior = (d1 + d2 + d3) / 3m;
                // A flat band is not a bend. Require a declining run and discrete deceleration.
                // The 65% ratio mirrors existing recognition; it is not a validated trading gate.
                if (d1 < 0m && d2 < 0m && d3 < 0m && latest > d3 &&
                    latest >= prior * 0.65m && prices[i - 1] < bands[i - 1] &&
                    prices[i] > prices[i - 1] && momentum[i] > momentum[i - 1])
                    bend = i;
            }

            var recovering = prices[^1] > prices[^2] && prices[^2] > prices[^3] &&
                             momentum[^1] > momentum[^2] && momentum[^2] >= momentum[^3];
            var name = bend < 0 ? (recovering ? "Preparing" : "None")
                : !recovering ? "Stalled"
                : prices[^1] >= bands[^1] || snapshotPrice > 0m && snapshotPrice >= bands[^1]
                    ? "TargetReached" : "Active";
            var age = bend < 0 ? -1 : count - 1 - bend;
            return new(name, age,
                $"BarsSinceBend={age}, Recovering={recovering}, Mid={bands[^1]}, " +
                $"CompletedClose={prices[^1]}, SnapshotPrice={snapshotPrice}");
        }
    }
}
