# Evaluation Rules

Main artifact:

Since 2026-10-17, the evaluation dataset includes saved probability/model/scope,
BrierLoss and trade-bounded target progress immediately after PlannedLossPct.
Full rebuild and incremental merge refresh historical forecast rows as well as
new evaluations. See [confidence feedback](CONFIDENCE_FEEDBACK.md) for formulas,
coverage statuses and the forecast-history exception to report filters.

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

Use [current success criteria](../../../guides/success-criteria.md) for the
canonical objective, participation, unfinished horizons and calibration.
The 2026-10-17 priorities are Win count/share among entered plans, winning
profit percentages, then agreement of DiagnosticsEstimatedHitRatePct with
outcomes. Minimum: a dependable rank-1 winner, with 1.5% profit sufficient.

## Interpretation

- EntryTime establishes participation under the saved evaluation plan.
  Show entered Win/Loss/Open and published NoEntry separately. Show both
  Win/entered and resolved Win/(Win+Loss) with explicit denominators.
- Read profit relative to entry and the evaluated exit. PlannedProfitPct is
  a target until reached. Future maxima and scan-wide amplitude are not profit.
- AmplitudePct is diagnostic: high amplitude plus NoEntry can suggest an
  entry problem; high amplitude plus Loss can suggest bad timing, stops,
  direction or ranking. Diagnose the path instead of assigning the cause
  from amplitude alone. Neither counts as a winning trade.
- A Win with 1.5% profit can satisfy the minimum goal even below 10% amplitude.
  Do not classify it as scanner failure solely on that threshold.
- Calibrate saved confidence within the matching model/scope/plan policy.
  Current WinGivenEntryAndResolution is compared with Win/(Win+Loss);
  disclose Open/NoEntry and incomplete coverage. Historical amplitude
  estimates are not forecasts of Win under the current plan.
- Scanner features use only the saved pre-outcome snapshot. Current groups
  are BellUp, ReversalHook and Other; legacy family labels do not establish
  current admission. A pattern match alone does not prove a win.

## Recent project behavior

### Report group ordering (2026-10-22)

BuildEvaluationDataset.SortColumns in agentsettings.json explicitly orders
CandidateGroup as BellUp, ReversalHook, Other, followed by legacy Runaway and
Reversal. The former configuration only listed the legacy names, so modern
groups fell back to alphabetical BellUp/Other/ReversalHook despite correct
class defaults. ScanTime descending remains the first key. Subtype prefixes
are not group keys. Other's existing BellUp-diagnostic/amplitude ordering and
CandidateDisplayRank ordering within admitted groups remain unchanged.

To reorder an existing evaluation dataset without broker calls, metric or
outcome recalculation, row deduplication or report filters, run:

```powershell
dotnet run --project IbSwingTrader -- sort-evaluation-dataset
```

The user runs this command/builds. The regression in EvaluationConfidenceChecks
loads the shipped settings to catch a configuration/default mismatch.

### Amplitude reference (2026-10-21)

User-selected AmplitudePct formula, rounded to two decimals:

- Entered (`EntryTime` present): `(MaxPrice / EntryPrice - 1) * 100`.
  Do not infer participation from nonzero plan prices. Extrema order does
  not invert this entry-relative result.
- Unentered diagnostics, including Other: if `MinTime < MaxTime` and the
  minimum is below ScanPrice, use
  `(MaxPrice / MinPrice - 1) * 100`. If `MaxTime < MinTime`, use ScanPrice
  as reference and retain the existing negative max-first order marker.
  The marker describes path order, not a realized loss.
  A minimum above ScanPrice is not a drop below the scan reference: use
  ScanPrice even when that minimum occurs before the maximum (SMCI's
  2026-10-20 morning row: scan31.53, min31.54, max37.66 gives19.44%).
- Equal or missing extrema times cannot prove min-first: use ScanPrice,
  with no order-based sign inversion. This does not reconstruct the first
  post-scan tick; extrema timestamps are the available path proxy.
- Prefer saved positive MaxPrice/MinPrice. If absent, reconstruct from
  ScanPrice and MaxPct/MinPct. Missing minimum falls back to ScanPrice;
  missing maximum or invalid required reference yields zero.

This changes the reference, not the saved observation horizon: MaxPrice is
still the scan-wide observed maximum, which may precede entry or follow a
stop/exit. Amplitude is not actual profit or a claim of executable upside.
Use TradeMfePct and TargetProgressPct for conservative post-entry,
stop-bounded excursion. Win/Loss/Open/NoEntry and BrierLoss are unchanged.

BuildRow, incremental merge, full rebuild and historical row normalization
use the same helper. `refresh-evaluation-feedback` also recalculates
AmplitudePct and GroupLabel for every saved report row, without date or
amplitude filtering, new broker requests or outcome replay. It preserves
row ordering. This permits historical refresh from existing price/time
fields; dated investigations retain their original values and definitions.

Prepared user-run regression command:

```text
dotnet run --project tests/EvaluationAmplitudeChecks
```

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
Scan-wide extrema retain their market-history calculation; AmplitudePct
uses the reference policy above.

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
