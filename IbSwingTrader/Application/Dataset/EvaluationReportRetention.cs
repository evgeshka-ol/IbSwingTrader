namespace IbSwingTrader.Application.Dataset
{
    public static class EvaluationReportRetention
    {
        public static bool IsUnresolvedPlan(EvaluationDatasetRow row, DateTime asOf, int forwardDays) =>
            row.EntryPrice > 0m && row.ExitPrice > row.EntryPrice &&
            row.StopLoss > 0m && row.StopLoss < row.EntryPrice &&
            !string.Equals(row.CandidateGroup, "Other", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.CandidateSource, "Other", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.CandidateSource, "DiagnosticRejected", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.Outcome, "Win", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(row.Outcome, "Loss", StringComparison.OrdinalIgnoreCase) &&
            ((row.EntryTime.HasValue && string.Equals(row.Outcome, "Open", StringComparison.OrdinalIgnoreCase)) ||
             (asOf.Date - row.ScanTime.Date).Days < Math.Max(1, forwardDays));

        public static (List<EvaluationDatasetRow> Current, List<EvaluationDatasetRow> Archive)
            Partition(List<EvaluationDatasetRow> rows, int keepRuns, DateTime asOf, int forwardDays)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keepRuns);
            var recent = rows.Select(x => x.ScanTime).Distinct()
                .OrderByDescending(x => x).Take(keepRuns).ToHashSet();
            var current = new List<EvaluationDatasetRow>();
            var archive = new List<EvaluationDatasetRow>();
            foreach (var row in rows)
                (recent.Contains(row.ScanTime) || IsUnresolvedPlan(row, asOf, forwardDays) ? current : archive).Add(row);
            return (current, archive);
        }
    }
}
