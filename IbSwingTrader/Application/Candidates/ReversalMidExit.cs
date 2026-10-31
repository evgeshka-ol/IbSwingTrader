namespace IbSwingTrader.Application.Candidates
{
    public readonly record struct ReversalMidExitTarget(decimal Price, string Profile);

    public static class ReversalMidExit
    {
        public static ReversalMidExitTarget Calculate(decimal entry, decimal dailyMid)
        {
            if (entry <= 0m || dailyMid <= entry)
                throw new ArgumentException("ReversalHook needs an unreached Daily mid above the fresh entry");
            var cap = entry * 1.10m;
            var price = decimal.Floor(Math.Min(dailyMid, cap) * 100m) / 100m;
            if (price <= entry)
                throw new ArgumentException("ReversalHook has no positive tradable room to the Daily mid");
            return new(price, dailyMid <= cap ? "reversal-daily-mid" : "reversal-10pct-cap");
        }

        // A fresh EMA bend can lag the actual return-to-mid price leg (AMD).
        // A seed's close is known at confirmation; a same-bar high may predate it.
        public static string? FindCompletedReturn(string timeframe, DateTime? signalBar,
            IReadOnlyList<Candle> completedDaily, DateTime decisionAt)
        {
            if (!signalBar.HasValue || timeframe is not ("D1" or "Daily" or "H4") || completedDaily.Count < 20)
                return null;
            var confirmedAt = timeframe == "H4" ? signalBar.Value.AddHours(4)
                : signalBar.Value.Date.AddHours(16);
            if (completedDaily.Any(x => x.Close <= 0m || x.Time.Date.AddHours(16) > decisionAt) ||
                completedDaily.Zip(completedDaily.Skip(1), (a,b) => b.Time <= a.Time).Any(x => x))
                throw new ArgumentException("Invalid completed native D1 history for reversal exit stage");
            var features = FeatureEngine.CalculateNativeReversalHistory(completedDaily.ToList());
            for (var i = 19; i < completedDaily.Count; i++)
            {
                var bar = completedDaily[i];
                var mid = features[i].DailyBollingerMidBand;
                if (bar.Time.Date.AddHours(16) < confirmedAt || mid <= 0m)
                    continue;
                var closeProof = bar.Close >= mid;
                var laterHighProof = bar.Time >= confirmedAt && bar.High >= mid;
                if (closeProof || laterHighProof)
                    return FormattableString.Invariant(
                        $"ReversalDailyMidCompleted(Timeframe={timeframe}, SignalBar={signalBar:O}, SignalConfirmedAt={confirmedAt:O}, TouchDailyBar={bar.Time:O}, ConfirmedAt={bar.Time.Date.AddHours(16):O}, Mid={mid}, Price={(closeProof ? bar.Close : bar.High)}, Source={(closeProof ? "CompletedClose" : "LaterCompletedHigh")})");
            }
            return null;
        }
    }
}
