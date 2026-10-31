namespace IbSwingTrader.Application.Candidates
{
    public static class PublicationPriceGuard
    {
        public static bool IsFresh(TradePlanInfo plan, DateTime now, int liveMaxAgeSeconds)
        {
            var source = plan.ReferencePriceSource ?? string.Empty;
            var maxAge = source is "M5PartialClose" or "M5CompletedClose" ? TimeSpan.FromMinutes(5)
                : source.StartsWith("IBLive", StringComparison.Ordinal) ? TimeSpan.FromSeconds(Math.Max(1, liveMaxAgeSeconds))
                : TimeSpan.Zero;
            return maxAge > TimeSpan.Zero && plan.LiveReferencePrice > 0m &&
                plan.ReferencePriceTime is DateTime priceTime &&
                plan.ReferencePriceObservedAt is DateTime observedAt &&
                priceTime <= now.AddSeconds(2) && observedAt <= now.AddSeconds(2) &&
                now - priceTime <= maxAge && now - observedAt <= maxAge;
        }

        public static bool RejectIfStale(CandidateDetails candidate, DateTime now, int liveMaxAgeSeconds)
        {
            if (CandidateGroups.IsOther(candidate) || IsFresh(candidate.TradePlan, now, liveMaxAgeSeconds))
                return false;
            var plan = candidate.TradePlan;
            plan.EntryPrice = plan.ExitPrice = plan.StopLoss = plan.StopLimitPrice = 0m;
            plan.ProfitPercent = plan.LossPercent = 0m;
            plan.ExitProfile = null;
            plan.PublicationRefreshStatus = "Unavailable";
            candidate.CandidateSource = "Other";
            candidate.PatternVerdictReason += "; NotReady=PublicationPriceExpired";
            if (candidate.Diagnostics != null)
            {
                candidate.Diagnostics.EstimatedHitRatePct = null;
                candidate.Diagnostics.EstimatedHitRateModel = string.Empty;
                candidate.Diagnostics.EstimatedHitRateScope = string.Empty;
            }
            return true;
        }
    }
}
