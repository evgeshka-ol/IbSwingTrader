# Run-based report retention and retirement of misleading probabilities

Implemented2026-11-10 at the user's request after the
[report audit](../investigations/2026-11/investigation-2026-11-10-report-size-and-feedback.md).
Build/runtime verification is pending user-run checks.

## Working report and complete history

`BuildEvaluationDataset.KeepRecentScanRuns=30` is shipped and is the default
when the setting is absent. Distinct ScanTime values define runs; each retained
run keeps all of its tickers/presets/groups. Full rebuild, incremental merge and
feedback refresh share the partition. Old saved forecasts no longer exempt
rows from the working-file window. In this mode legacy RecentScanDays,
MinScanTime and MinAmplitudePct filters are bypassed so evidence is archived
instead of lost. Null/zero explicitly restores legacy filtering; negatives fail
on save. Sort-only still sorts only current rows, without loading history.

Positive valid playable entered Open remain regardless of age. Other and
DiagnosticRejected do not count as unresolved playable plans even if historical
rows contain nonzero prices. Nonresolved unfilled plans remain outside the
window only while their CandidateEvaluation.ForwardEvaluationDays period is
active; that provider is injected by the existing DI registration. Expired rows
are archived without changing Outcome. Stale entered Open are not automatically
closed. The working report can contain more than30 batches due to these plans.

`Data/datasets/evaluation-dataset-archive.csv` stores the complete canonical
history, including current rows, all saved forecasts, series and explanations.
No new CSV schema is introduced. Key:Ticker/PresetScanCode/ScanTime; latest
EvaluatedAt wins, current wins ties. History is loaded when run retention is
enabled, so a complete rebuild does not erase prior dataset-only evidence;
increasing the run count can recover older rows. Newly resolved old plans
update history and can leave the working file.

Each file uses a unique same-directory temporary file and overwrite move.
Complete archive is saved first; only then is the working report replaced.
Failure before that leaves current unchanged; failure between writes leaves
complete history recoverable, including any rows moving back into the report.
Temporary files are removed on failure. Existing single-writer operation is
assumed; there is no new concurrent-writer lock or multi-file transaction.

Archive is a CSV history, not the dated gzip audit snapshot. Read it directly,
or deduplicate its union with current rows, for all-date confidence analysis.
Do not analyse only the recent working report for long-term calibration.
The archive grows and is read/refreshed during updates: this change limits
the file opened by the user, not the total historical storage/processing cost.

An offline count on the inspected14547-row snapshot projects6383 working
rows,8164 removed from that view and156 older actionable rows. Re-serializing
those source rows in Python was approximately63.69MiB versus110.1MiB original.
This is a static estimate, not a C# execution result or a size guarantee for
later scans. Choose a smaller KeepRecentScanRuns if a shorter report is wanted.
No existing data/report/archive was migrated or overwritten by Codex.

## New-signal confidence

Production `BellUpWinProbability.Estimate` returns null. New signals therefore
carry null EstimatedHitRatePct and empty model/scope; console displays Win~=n/a
with an updated legend. CandidateFinder clears any reused forecast at final
publication. Historical candidate/evaluation percentages and metadata are not
changed, and historical feedback scoring still works.

The original ridge coefficients/model ID remain reproducible under explicitly
named `EstimateUncalibratedForResearch`; no production caller uses it. The
audit's53 resolved EMA/version8 BellUp had mean prediction89.99% versus49.06%
Win, with48 other entered plans still Open. This does not support simply
halving probabilities, substituting recent Win-rate or claiming calibrated
replacement coefficients. A future replacement needs declared policy cohorts,
temporal/ticker separation, held-out evaluation and adequate outcome coverage.

The score/ranking formulas, pattern gates and plan prices are unchanged. Since
all new forecasts are absent, display uses its existing non-probability
tie-breakers (NextDayRank/profit/score/ticker); relative new candidate order can
therefore change from the former confidence-first display. Existing historical
display ranks and forecasts are retained, not reconstructed with today's model.

## Verification

Extended EvaluationConfidenceChecks covers whole runs/presets, old saved
forecasts, old entered Open, expiry of pending entry plans, exclusion of legacy
diagnostic prices, full/incremental/feedback consistency, sort-only behavior,
window expansion, updated resolved outcomes, archive/current write failures,
retry without loss/duplicates, retirement and research reproducibility.
Legacy-mode feedback checks remain explicitly legacy, including exemptions.

Codex performed focused diff/source review, JSON validation, project/source
reference inspection and documentation-link checks; the offline row count was
executed against local CSV only. No builds, C# tests, broker requests, application
commands or report migration were run. No runtime pass is claimed.

User-run commands from repository root:

```powershell
dotnet build IbSwingTrader/IbSwingTrader.csproj
dotnet run --project tests/EvaluationConfidenceChecks
dotnet run --project tests/CandidateCsvChecks
dotnet run --project tests/ExecutionCsvChecks
```

After successful checks, the next normal evaluate-candidates update applies
retention. The existing offline refresh-evaluation-feedback command also applies
it while scoring history, with no broker connection; it refreshes feedback and
existing amplitude/group derivations as before, not just truncation. Builds/tests
and application commands remain user-run under repository guidance.

Current rules: [confidence/history](../project-skills/ibswingtrader-evaluation/references/CONFIDENCE_FEEDBACK.md),
[settings](../project-skills/ibswingtrader-scanner/references/SETTINGS_MAP.md).
