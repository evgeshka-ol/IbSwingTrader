namespace IbSwingTrader.Domain.Candidates
{
    public static class CandidateDisplayOrder
    {
        public static IOrderedEnumerable<CandidateDetails> Order(IEnumerable<CandidateDetails> candidates) => candidates
            .OrderByDescending(x => x.Scan.ScanTime)
            .ThenByDescending(x => x.Diagnostics?.EstimatedHitRatePct ?? decimal.MinValue)
            .ThenByDescending(x => CandidateGroups.IsOther(x) &&
                (x.PatternVerdictReason.StartsWith("BellUp confirmed on ", StringComparison.OrdinalIgnoreCase) ||
                 x.PatternVerdictReason.StartsWith("DailyTransitionBellUp detected on Daily", StringComparison.OrdinalIgnoreCase) ||
                 x.PatternVerdictReason.StartsWith("ReversalHookPreparing detected on D1", StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(x => x.Score.NextDayRank ?? decimal.MinValue)
            .ThenByDescending(x => x.TradePlan.ProfitPercent)
            .ThenByDescending(x => x.Score.Score)
            .ThenBy(x => x.Ticker, StringComparer.OrdinalIgnoreCase);
    }
}
