# Confidence and target-progress feedback

Current contract, 2026-10-17. Feedback columns in `evaluation-dataset.csv`
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
on reused rows, including dataset-only historical results. An existing saved
forecast exempts its evaluated row from `RecentScanDays`, `MinScanTime`, and
`MinAmplitudePct` pruning: keep all available forecast dates and avoid selecting
calibration examples by their later amplitude. Other rows retain normal filters.
Candidates without an evaluation do not acquire invented outcomes.

The update preserves saved outcomes and forecasts. It does not rerun closed
trades, fetch broker history, retrain, or calibrate the current model. Missing
history remains explicit. The dedicated offline command refreshes feedback for
every existing dataset row, without report filters or a TWS connection:

```powershell
dotnet run --project IbSwingTrader -- refresh-evaluation-feedback
```

This command preserves row order and existing trade/outcome values; it updates
the feedback columns using saved candidates, evaluations and cache. The normal
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
