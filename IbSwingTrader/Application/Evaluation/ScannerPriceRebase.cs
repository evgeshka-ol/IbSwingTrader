namespace IbSwingTrader.Application.Evaluation
{
    public static class ScannerPriceRebase
    {
        // Restore the saved scan reference when rebuilding legacy evaluation rows.
        // Trade outcomes and entry-relative metrics are independent of this reference.
        public static void Apply(CandidateEvaluationResult evaluation, decimal scannerPrice)
        {
            if (scannerPrice <= 0m)
                return;
            evaluation.StrategyVersion = 7;
            if (evaluation.ScanPrice == scannerPrice)
                return;
            var previous = evaluation.ScanPrice;
            decimal? Rebase(decimal? pct, decimal? price = null)
            {
                var absolute = price ?? (previous > 0m && pct.HasValue
                    ? previous * (1m + pct.Value / 100m) : (decimal?)null);
                return absolute.HasValue
                    ? decimal.Round((absolute.Value / scannerPrice - 1m) * 100m, 2, MidpointRounding.AwayFromZero)
                    : null;
            }
            evaluation.ScanMovePct = Rebase(evaluation.ScanMovePct,
                evaluation.CurrentPrice > 0m ? evaluation.CurrentPrice : null);
            evaluation.MaxPct = Rebase(evaluation.MaxPct, evaluation.MaxPrice);
            evaluation.MinPct = Rebase(evaluation.MinPct, evaluation.MinPrice);
            evaluation.MaxPctBeforeEntry = Rebase(evaluation.MaxPctBeforeEntry, evaluation.MaxPriceBeforeEntry);
            evaluation.MinPctBeforeEntry = Rebase(evaluation.MinPctBeforeEntry, evaluation.MinPriceBeforeEntry);
            evaluation.MinBeforeMaxPct = Rebase(evaluation.MinBeforeMaxPct);
            evaluation.OptimalEntryDiscountPct = evaluation.MinBeforeMaxPct.HasValue
                ? Math.Max(-evaluation.MinBeforeMaxPct.Value, 0m) : null;
            evaluation.AdverseMoveBeforeRunPct = evaluation.OptimalEntryDiscountPct;
            evaluation.ScanPrice = scannerPrice;
        }
    }
}
