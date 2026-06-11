# Evaluation Rules

Main artifact:

- `Data/datasets/evaluation-dataset.csv`

## What matters most

### Scanner oracle

Use `AmplitudePct` first.

Suggested mental buckets:

- `>= 10%` strong / interesting
- `5% to <10%` borderline
- `<5%` weak / likely poor list quality

For both scanner families:

- `TodayResearchLikeCandidates` should be judged by whether it becomes the next research dataset.
- `ReversalCandidates` should still produce meaningful amplitude, normally `> 10%`.
- If amplitude is below 10%, treat that as scanner failure, even when the trade plan correctly avoids entry.
- Family membership is fixed by the last closed daily Bollinger mid split:
  - below daily mid on the last closed daily bar -> `ReversalCandidates`
  - at or above daily mid on the last closed daily bar -> `TodayResearchLikeCandidates`
- Do not change the family boundary during amplitude or trade-plan tuning; only
  list promotion, ranking, and exit shaping should move after the split is correct.

### Trade plan oracle

Use:

- `EntryTime`
- `ExitTime`
- `Outcome`
- `PlannedProfitPct`
- `PlannedLossPct`

These tell you whether execution worked after the scanner already found the move.

The number of winners is a trade-plan conversion metric.

Many `Win` rows mean the trade plan is converting scanner opportunities.
Many `Loss`, `NoEntry`, or `Open` rows with strong amplitude mean the trade plan is failing to convert movement.

## Interpretations

### High amplitude + NoEntry

Scanner likely found valid movement.

This is usually a `TradePlan` issue, not a list issue.

### Low amplitude + NoEntry

This is usually not a trade-plan issue.

The scanner likely surfaced a weak candidate.

### Win with modest amplitude

Can still be fine for execution, but does not prove scanner recall quality.

### Loss with large amplitude

Can indicate:

- wrong side of movement
- late entry
- bad plan

Do not use this alone to condemn the scanner. Large amplitude means the scanner
still found movement; the failure is usually downstream unless the move direction
or setup family was wrong.

### Many winners but low amplitude

This can make the trade plan look good while the scanner is still weak.

Do not let a high win count hide weak scanner recall/ranking.

## Recent project behavior

The project now recalculates amplitude for `NoEntry`.

Legacy rows can be backfilled through:

- `BuildEvaluationDataset.BackfillLegacyNoEntryZeroAmplitudeDays`
