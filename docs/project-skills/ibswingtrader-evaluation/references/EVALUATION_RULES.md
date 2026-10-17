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

### Entry and target in one M5 (2026-10-14)

The first entry bar can contain a target high that occurred before entry,
as demonstrated by VFC, scan 2026-10-13 04:50:25, entry 16.25 / target 16.57.
Its 09:45 M5 was O16.55/H16.57/L16.18/C16.22; the user's M1 chart shows
the target before the later downward crossing of entry. Candle color alone
does not establish this ordering.

When that entry M5 touches the target, request exactly five M1 ending at
parent start + five minutes (`300 S`, one-minute bars), with no range-gap
expansion or overlap. Reuse a complete cached five-minute window. Broker
retries, if needed, request the same bounded window. Missing M1 coverage
does not trigger downloads of the session, ticks or smaller bars.

Require all five minute starts and valid OHLC. Drop minutes before the first
entry touch, update EntryTime to that minute, then replay the suffix followed
by ordinary later M5. Legacy Candle.Timeframe metadata can be unset; the
parent interval is known to be five minutes from the evaluator's M5 source.

If the entry minute opens above entry, its high may precede entry. Use a
separate conservative replay candle with Open=entry, High=max(entry, Close),
and its observed Low/Close: an ending close at/above target proves an
after-entry recovery, whereas a high alone does not. If both stop and exit
are still within the same replay minute, conservatively prefer stop and log
the unresolved ordering; never download finer data or infer Win from color.

Without complete matching M1, suppress the parent's unconfirmed upside and
retain its downside conservatively, then continue with subsequent M5. This
can undercount a genuine intraminute winner, rather than inventing one.
Replay candles are in-memory bounds, never replacement market bars in cache.
Post-entry excursion metrics and the final correction loop use this same
replay, so the original parent high cannot silently restore a rejected Win.
Scan-wide amplitude retains its original market-history calculation.

Logs: `Entry-bar M1 window`, `Entry-bar sequence replay/unresolved/unavailable`,
`Entry-bar target not confirmed`, `Entry-minute exit/stop order unresolved`.
Existing stop/target resolution for other M5 bars remains separate.

Already saved Win rows, including VFC, are not automatically reevaluated with
`AllowedOutcomesToEvaluate=["Open"]`. This change governs evaluations that
actually run; it does not rewrite historical results or broaden outcome
selection. Rechecking a saved Win requires a separately authorized replay.

The project now recalculates amplitude for `NoEntry`.

Legacy rows can be backfilled through:

- `BuildEvaluationDataset.BackfillLegacyNoEntryZeroAmplitudeDays`
