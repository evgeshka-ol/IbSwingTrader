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

### Trade plan oracle

Use:

- `EntryTime`
- `ExitTime`
- `Outcome`
- `PlannedProfitPct`
- `PlannedLossPct`

These tell you whether execution worked after the scanner already found the move.

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

Do not use this alone to condemn the scanner.

## Recent project behavior

The project now recalculates amplitude for `NoEntry`.

Legacy rows can be backfilled through:

- `BuildEvaluationDataset.BackfillLegacyNoEntryZeroAmplitudeDays`
