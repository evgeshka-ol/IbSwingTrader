# Workflow checklist

The user runs application commands, builds and tests. Codex analyzes saved
artifacts and edits source/documentation within the repository boundary.

## Primary outcome loop

Follow [success criteria](../../../guides/success-criteria.md).

1. Inspect the original rank-1 playable row per scan, retaining group and
   ranking scope. Record Win/Loss/Open/NoEntry without substituting another row.
2. Join candidate snapshots and evaluation results by ticker and ScanTime.
3. Report entered Win/Loss/Open, winning profit percentages and NoEntry
   separately, with denominators and observation horizons.
4. Compare saved DiagnosticsEstimatedHitRatePct with compatible resolved
   outcomes; retain model/scope and plan-policy cohorts, calibration sample
   sizes, unresolved Open and repeated ticker/date dependencies.
5. Diagnose recognition, ranking, price freshness or execution problems and
   validate the proposed change on later observations.

Minimum goal: a dependable rank-1 winner; 1.5% profit is sufficient. This is
an objective, not proof of a guaranteed outcome.

## Additional research loop

The user generates candidates, evaluates them later and builds the research
dataset. Compare yesterday's scanner to today's movers as additional recall
evidence. Identify fully missed symbols, seen-but-not-promoted cases and
ranking gaps. Inspect saved series and rejection reasons before assigning a
cause. Amplitude is useful for explaining movement but does not turn Loss,
NoEntry or unresolved Open into a Win, or invalidate a modest profitable trade.
