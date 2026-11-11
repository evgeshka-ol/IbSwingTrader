# Confidence and target-progress feedback

Current contract, updated2026-11-10. Feedback columns in `evaluation-dataset.csv`
immediately follow `PlannedLossPct`, in this order:

1. `DiagnosticsEstimatedHitRatePct`
2. `DiagnosticsEstimatedHitRateModel`
3. `DiagnosticsEstimatedHitRateScope`
4. `BrierLoss`
5. `ConfidenceEvaluationStatus`
6. `TradeMfePct`
7. `TargetProgressPct`
8. `TargetProgressStatus`

## Saved forecast and probability error

Since2026-11-10 the experimental BellUp ridge is retired for new publication:
new percent is null, model/scope empty, console Win~ n/a. This does not replace
historical forecasts, refit coefficients or assert a new probability calibration.
Original saved forecasts and their compatible Win/Loss feedback remain valid
audit inputs, including when retained only in the complete report archive.

Restore the forecast and its metadata together from the exact candidate key
`Ticker + PresetScanCode + ScanTime` (seconds), falling back to saved evaluation
metadata or the existing dataset row. Never run today's probability model on
historical candidates to manufacture their original forecasts. Newly evaluated
results also retain these three forecast fields in evaluations CSV.

For a nonempty saved model and scope `WinGivenEntryAndResolution`, with an
actual `EntryTime` and resolved `Win`/`Loss`:

```text
p = DiagnosticsEstimatedHitRatePct / 100
y = 1 for Win, 0 for Loss
BrierLoss = (p - y)^2
```

The per-row contribution is in [0, 1]; lower mean loss is better. CSV retains
up to ten decimal places for this field rather than the ordinary two-decimal
numeric formatting. A forecast of zero is valid, not missing.

`ConfidenceEvaluationStatus` explains blank scores: `MissingForecast`,
`InvalidForecast` (outside 0–100), `UnsupportedScope` (including missing model
metadata), `NoEntry`, or `Unresolved`. Eligible rows use `Scored`.
Old unversioned percentages and amplitude estimates remain visible but do not
acquire Win semantics. Open and NoEntry never become negative training labels.

## Progress during the saved trade

```text
TradeMfePct = 100 * (peak / EntryPrice - 1)
TargetProgressPct = 100 * clamp((peak - EntryPrice) / (ExitPrice - EntryPrice), 0, 1)
```

Peak starts at entry, so MFE is nonnegative. Only observations after entry and
before closure contribute. `ExitPrice` is the saved target, not a later maximum.
A confirmed saved Win establishes a target fill: progress 100%, MFE equal to
the plan's achieved target return, status `FinalFromOutcome`. This can be
restored even when old intraday cache has expired; it does not revalidate the
historical outcome or infer actual broker fills/slippage.

For Loss use `StopTime`, not a later `ExitTime`/target touch. If only a dataset
row survives, recover a conservative first stop-touch boundary from cached M5,
bounded by its reported closure/observation horizon. If no boundary can be
recovered, leave progress blank. Open uses the saved observation horizon and
remains provisional.

Use cached M5 and complete cached five-minute M1 windows to refine entry and
stop parents. Discard minutes before saved entry. An entry minute opening above
entry has an uncertain pre-entry high: retain only its guaranteed entry-to-close
upside. Without complete M1, apply the analogous bound to the first M5. Never
count the stop candle's full high: it may follow the stop; retain only a known
active opening price and earlier highs. Stop at the first observed stop touch.
Missing/invalid required bars produce no metric; gaps and uncertain intrabar
ordering produce conservative bounds. No broker requests are made for feedback.

Progress statuses:

- `FinalFromOutcome`: confirmed target fill in saved Win.
- `Final` / `FinalConservative`: closed Loss, with exact/bounded observations.
- `Provisional` / `ProvisionalConservative`: still-open observation horizon.
- `InsufficientData`, `NoEntry`, `InvalidPlan`, `Unresolved`: no numeric progress.
- Since2026-10-30 `EntryObservationUnconfirmed`: a saved Loss/Open entry lacks
  a valid positive-volume parent touch; no numeric progress. Zero-volume M1/M5
  cannot contribute an excursion or prove stop/target order. Saved Win still
  uses FinalFromOutcome; feedback refresh never replays a saved outcome.
- `HistoryConflict`: the reconstructed path reaches the target despite a saved
  non-Win. Keep the original outcome and expose the conflict for investigation.

These fields do not reuse `MaxPrice` or `ExitMissPct`, which can describe a
broader post-scan window. Progress 90% is not a 90% probability or a fractional
Win. It measures proximity to this particular plan's target.

## Historical population and refresh

Both full dataset rebuild and incremental evaluation merge refresh feedback
on reused rows, including dataset-only historical results. With
`BuildEvaluationDataset.KeepRecentScanRuns=30`, run retention replaces legacy
RecentScanDays/MinScanTime/MinAmplitudePct report filters. Keep complete batches
by distinct ScanTime, not dates, presets or rows. Forecast presence does not
exempt a row from the working window; its evidence remains in
`datasets/evaluation-dataset-archive.csv`. This archive is a complete canonical
history, including working rows, not just the removed tail. It preserves all
forecast dates regardless of amplitude and also nonforecast/rejected evidence.

Positive valid playable plans with entered Open stay in the working report
regardless of age. Other/DiagnosticRejected are not playable even when legacy
rows have positive price fields. Other unresolved playable plans are retained
outside the window only while their configured ForwardEvaluationDays period
has not expired. Expiry archives the row without rewriting its outcome.
Thus working rows may exceed the requested number of batches due to older
actionable plans. Long-stale entered Open remain explicit; retention is not an
automatic closure/re-evaluation rule.

History and working rows merge by Ticker/PresetScanCode/ScanTime with the latest
EvaluatedAt winning; current wins ties. Updates can resolve historical plans;
increasing the window can restore archived batches. Archive is replaced via
a same-directory temporary file before the reduced working report, so failed
writes leave recoverable history. Writes assume the existing single-writer
workflow; no cross-process locking is introduced.

Null/zero KeepRecentScanRuns opts into legacy date/amplitude filtering and the
historical forecast exemption; it does not automatically load the report archive.
Negative values are rejected on save. Candidates without an evaluation do not
acquire invented outcomes. Independent calibration reads the complete archive
or the key-deduplicated union with the working CSV, never only the recent view.

The update preserves saved outcomes and forecasts. It does not rerun closed
trades, fetch broker history, retrain, or calibrate the current model. Missing
history remains explicit. The dedicated offline command refreshes feedback for
every existing report/history row without a TWS connection:

```powershell
dotnet run --project IbSwingTrader -- refresh-evaluation-feedback
```

In run-retention mode this command reapplies the working window and configured
sort order after feedback; retained outcomes and original forecasts remain.
In legacy mode it applies no report date/amplitude filtering. It updates
feedback using saved candidates, evaluations and cache. The normal
evaluation merge and full dataset rebuild also refresh feedback. Merely opening
an old CSV cannot populate new columns.

## Assessing improvement

Use only compatible models/scopes and plan policies. Report mean Brier loss,
sample count, and confidence buckets with mean forecast, observed Win/(Win+Loss),
and their gap in percentage points. Brier measures overall probability quality;
bucket frequencies specifically reveal calibration. Report Open/NoEntry and
coverage alongside the resolved population. Keep BollingerModel cohorts distinct.

Fit/calibrate on earlier dates and assess on later unseen dates. Repeated scans
of one ticker are dependent; do not randomly split overlapping episodes between
training and validation. Assess rank-1 wins and profit alongside probability
quality. Target progress helps diagnose plans but is not a replacement label
for training the Win model.

Checks (run by the user):

```powershell
dotnet run --project tests/EvaluationConfidenceChecks
dotnet run --project tests/ExecutionCsvChecks
```
