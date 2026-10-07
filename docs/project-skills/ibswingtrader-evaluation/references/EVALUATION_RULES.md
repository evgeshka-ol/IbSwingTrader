# Evaluation Rules

Main artifact:

Price contract since 2026-10-07: ScanPrice is the price saved by the scanner
in candidates.csv, not a later M5 open. MaxPct, MinPct, ScanMovePct and
scan-relative timing percentages use that reference. A saved historical
reference can be stale; changing the report does not make it a fresh quote.
Trade outcomes and planned profit/loss remain based on plan entry/exit/stop.
StrategyVersion=7 marks the new contract; rebuilds repair matched legacy
rows from their exact candidate snapshots. Without a snapshot the old price
cannot safely be reconstructed from future bars and is left unchanged.

- `Data/datasets/evaluation-dataset.csv`

Current candidate groups are `BellUp`, `ReversalHook`, and `Other`. Historical
rows may contain the former `Runaway`/`Reversal` labels; use the saved group,
source, and scan-time pattern reason when distinguishing current playable
setups from legacy family labels.

## What matters most

### Scanner oracle

Use `AmplitudePct` first.

Suggested mental buckets:

- `>= 10%` strong / interesting
- `5% to <10%` borderline
- `<5%` weak / likely poor list quality

For both playable pattern groups:

- `BellUp` should be judged by whether it becomes the next research dataset.
- `ReversalHook` should still produce meaningful amplitude, normally `> 10%`.
- If amplitude is below 10%, treat that as scanner failure, even when the trade plan correctly avoids entry.
- Do not use realized `AmplitudePct` before evaluation. Candidate pattern
  groups are `BellUp`, `ReversalHook`, and `Other`; the former Daily-mid
  `Runaway`/`Reversal` family split is legacy and does not define current
  group membership.
- Do not classify an isolated terminal H4/Daily jump as `BellUp`; the expansion
  must not be explained by one final spike in band width and momentum.

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
