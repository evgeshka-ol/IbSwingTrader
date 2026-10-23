namespace IbSwingTrader.Application.Candidates
{
    public static class ReversalPostBellUpCompletion
    {
        // A post-BellUp pullback targets Daily mid, rather than a new upper-band impulse.
        // Inputs must be the same completed native D1 prefix and fresh quote as publication.
        public static string? Find(ReversalOrigin origin, IReadOnlyList<Candle> completedDaily,
            decimal freshPrice, DateTime decisionAt)
        {
            if (origin.Kind != "PostBellUpRecovery" || !origin.RunStart.HasValue || completedDaily.Count < 20 ||
                completedDaily.Any(x => x.Time.Date.AddHours(16) > decisionAt || x.Close <= 0m) ||
                completedDaily.Zip(completedDaily.Skip(1), (a, b) => b.Time <= a.Time).Any(x => x))
                return null;
            var features = FeatureEngine.CalculateNativeReversalHistory(completedDaily.ToList());
            for (var i = 19; i < completedDaily.Count; i++)
            {
                var bar = completedDaily[i];
                // The initial downward-cross candle itself contains mid on the way DOWN.
                // Only a subsequent bar can establish a return from the below-mid episode.
                if (bar.Time.Date <= origin.RunStart.Value.Date)
                    continue;
                var mid = features[i].DailyBollingerMidBand;
                if (mid <= 0m)
                    continue;
                var high = bar.High > 0m ? Math.Max(bar.High, bar.Close) : bar.Close;
                if (high >= mid)
                    return FormattableString.Invariant(
                        $"PostBellUpMidCompleted(Timeframe=D1, RunStart={origin.RunStart:yyyy-MM-dd}, TouchBar={bar.Time:yyyy-MM-dd}, ConfirmedAt={bar.Time.Date.AddHours(16):O}, High={high}, Mid={mid}, Source={(bar.High > 0m ? "CompletedHigh" : "CompletedClose")})");
            }
            var lastMid = features[^1].DailyBollingerMidBand;
            if (lastMid > 0m && freshPrice >= lastMid)
                return FormattableString.Invariant(
                    $"PostBellUpMidCompleted(Timeframe=D1, RunStart={origin.RunStart:yyyy-MM-dd}, ObservedAt={decisionAt:O}, Price={freshPrice}, Mid={lastMid}, Source=FreshPrice)");
            return null;
        }
    }
}
