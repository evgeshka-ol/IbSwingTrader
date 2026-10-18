namespace IbSwingTrader.Application.Evaluation
{
    /// <summary>Feedback on the saved forecast and plan, never a new trade outcome.</summary>
    public static class EvaluationConfidenceMetrics
    {
        public static void ScoreForecast(EvaluationDatasetRow row)
        {
            row.BrierLoss = null;
            row.ConfidenceEvaluationStatus = "MissingForecast";
            if (!row.DiagnosticsEstimatedHitRatePct.HasValue)
                return;
            if (row.DiagnosticsEstimatedHitRatePct < 0m || row.DiagnosticsEstimatedHitRatePct > 100m)
            {
                row.ConfidenceEvaluationStatus = "InvalidForecast";
                return;
            }
            // Missing metadata and historical amplitude estimates must not be relabeled as Win forecasts.
            if (string.IsNullOrWhiteSpace(row.DiagnosticsEstimatedHitRateModel) ||
                row.DiagnosticsEstimatedHitRateScope != "WinGivenEntryAndResolution")
            {
                row.ConfidenceEvaluationStatus = "UnsupportedScope";
                return;
            }
            if (!row.EntryTime.HasValue)
            {
                row.ConfidenceEvaluationStatus = "NoEntry";
                return;
            }
            if (row.Outcome is not ("Win" or "Loss"))
            {
                row.ConfidenceEvaluationStatus = "Unresolved";
                return;
            }
            var p = row.DiagnosticsEstimatedHitRatePct.Value / 100m;
            var error = p - (row.Outcome == "Win" ? 1m : 0m);
            row.BrierLoss = error * error;
            row.ConfidenceEvaluationStatus = "Scored";
        }

        public static void MeasureProgress(
            EvaluationDatasetRow row, DateTime observationEnd, DateTime? stopTime,
            IReadOnlyList<Candle> m5, IReadOnlyList<Candle> m1)
        {
            row.TradeMfePct = null;
            row.TargetProgressPct = null;
            row.TargetProgressStatus = "NoEntry";
            if (!row.EntryTime.HasValue)
                return;
            if (row.EntryPrice <= 0m || row.ExitPrice <= row.EntryPrice ||
                row.StopLoss <= 0m || row.StopLoss >= row.EntryPrice)
            {
                row.TargetProgressStatus = "InvalidPlan";
                return;
            }
            if (row.Outcome == "Win")
            {
                // The saved evaluator confirms a fill at the target, not the rest of its candle's high.
                SetProgress(row, row.ExitPrice, "FinalFromOutcome");
                return;
            }
            if (row.Outcome is not ("Loss" or "Open"))
            {
                row.TargetProgressStatus = "Unresolved";
                return;
            }
            var isLoss = row.Outcome == "Loss";
            var end = isLoss ? stopTime : observationEnd;
            row.TargetProgressStatus = "InsufficientData";
            if (!end.HasValue || end.Value < row.EntryTime.Value || observationEnd < end.Value)
                return;

            var entry = row.EntryTime.Value;
            var parents = m5.Where(x => x.Time.AddMinutes(5) > entry && x.Time <= end.Value)
                .GroupBy(x => x.Time).Select(x => x.Last()).OrderBy(x => x.Time).ToList();
            if (parents.Count == 0 || parents[0].Time > entry ||
                (isLoss && parents[^1].Time.AddMinutes(5) <= end.Value))
                return;

            var minutes = m1.Where(x => x.Time >= parents[0].Time && x.Time < parents[^1].Time.AddMinutes(5))
                .GroupBy(x => x.Time).ToDictionary(x => x.Key, x => x.Last());
            var peak = row.EntryPrice;
            var conservative = false;
            DateTime? previousEnd = null;
            foreach (var parent in parents)
            {
                if (previousEnd.HasValue && parent.Time > previousEnd.Value)
                    conservative = true; // Includes overnight gaps: no claim of exact continuous coverage.
                previousEnd = parent.Time.AddMinutes(5);
                var first = parent.Time <= entry && entry < previousEnd.Value;
                var terminal = isLoss && parent.Time <= end.Value && end.Value < previousEnd.Value;
                var boundary = first || terminal;
                var sub = Enumerable.Range(0, 5)
                    .Select(i => minutes.GetValueOrDefault(parent.Time.AddMinutes(i))).ToList();
                if (boundary && sub.All(x => x != null && Valid(x)))
                {
                    if (terminal && !sub.Any(x => x!.Time >= entry && x.Low <= row.StopLoss))
                    {
                        SetProgress(row, Math.Max(peak, first ? row.EntryPrice : parent.Open), "FinalConservative");
                        return;
                    }
                    var activated = !first;
                    foreach (var minute in sub)
                    {
                        var bar = minute!;
                        if (bar.Time < entry || (!isLoss && bar.Time.AddMinutes(1) > observationEnd))
                            continue;
                        var entryMinute = false;
                        if (!activated)
                        {
                            if (bar.Low > row.EntryPrice || bar.High < row.EntryPrice)
                                continue;
                            activated = true;
                            entryMinute = true;
                        }
                        if (bar.Low <= row.StopLoss)
                        {
                            // Its high may be after the stop. Only an already active opening price is certain.
                            peak = Math.Max(peak, entryMinute ? row.EntryPrice : bar.Open);
                            SetProgress(row, peak, isLoss ? "FinalConservative" : "ProvisionalConservative");
                            return;
                        }
                        if (entryMinute && bar.Open > row.EntryPrice)
                        {
                            peak = Math.Max(peak, Math.Max(row.EntryPrice, bar.Close));
                            conservative = true;
                        }
                        else
                            peak = Math.Max(peak, bar.High);
                    }
                    if (first && !activated)
                        return;
                    if (terminal)
                        conservative = true; // Saved stop not reproduced by M1; do not extend beyond its parent.
                }
                else
                {
                    if (!Valid(parent))
                        return;
                    if (first && (parent.Low > row.EntryPrice || parent.High < row.EntryPrice))
                        return;
                    if (terminal || parent.Low <= row.StopLoss)
                    {
                        peak = Math.Max(peak, first ? row.EntryPrice : parent.Open);
                        SetProgress(row, peak, isLoss ? "FinalConservative" : "ProvisionalConservative");
                        return;
                    }
                    if (!isLoss && parent.Time.AddMinutes(5) > observationEnd)
                    {
                        conservative = true;
                        continue;
                    }
                    if (first && parent.Open > row.EntryPrice)
                    {
                        peak = Math.Max(peak, Math.Max(row.EntryPrice, parent.Close));
                        conservative = true;
                    }
                    else
                        peak = Math.Max(peak, parent.High);
                }
            }
            if (!isLoss && parents[^1].Time.AddMinutes(5) < observationEnd)
                conservative = true;
            SetProgress(row, peak, (isLoss ? "Final" : "Provisional") + (conservative ? "Conservative" : ""));
        }

        private static bool Valid(Candle bar) => bar.Open > 0m && bar.Close > 0m && bar.Low > 0m &&
            bar.High >= Math.Max(bar.Open, bar.Close) && bar.Low <= Math.Min(bar.Open, bar.Close);

        private static void SetProgress(EvaluationDatasetRow row, decimal peak, string status)
        {
            row.TradeMfePct = decimal.Round((peak / row.EntryPrice - 1m) * 100m, 4);
            row.TargetProgressPct = decimal.Round(
                Math.Clamp((peak - row.EntryPrice) / (row.ExitPrice - row.EntryPrice), 0m, 1m) * 100m, 4);
            row.TargetProgressStatus = row.Outcome != "Win" && peak >= row.ExitPrice
                ? "HistoryConflict"
                : status;
        }
    }
}
