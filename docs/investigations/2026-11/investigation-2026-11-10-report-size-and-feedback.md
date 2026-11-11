# Evaluation report: useful feedback and compact views

Date:2026-11-10. Status: descriptive offline audit, not strategy validation.

## Sources and preservation

Source: Data/datasets/evaluation-dataset.csv,115456602 bytes (110.1MiB),
14547 rows,125 columns, scan dates August24–November9. The canonical
Ticker/PresetScanCode/ScanTime key has no duplicates. File metadata says
November10. Source hash and latest evaluation timestamp are in
[summary.json](../../artifacts/evaluation-audit-2026-11-10/summary.json).

Only the offline Python audit was executed. No application commands, builds,
tests, TWS requests, outcome replay, model fitting or production changes.
The source remains byte-identical, verified by SHA256 before/after the audit.
Existing XLSX files and candidates/evaluations/cache are unchanged.

## Size and prepared views

| File | Rows | MiB | Role |
|---|---:|---:|---|
| Original full CSV | 14547 | 110.1 | Application input/output remains in place |
| evaluation-compact-all.csv | 14547 | 9.54 | All scalar fields,240-character reason summary, no indicator series |
| evaluation-plans.csv | 685 | 0.456 | Valid plans in current BellUp/ReversalHook groups, all available dates |
| evaluation-working.csv | 553 | 0.372 | Same current groups: latest30 scan-calendar days plus older unresolved plans |
| evaluation-dataset-full.csv.gz | 14547 | 34.01 | Exact full-source snapshot; decompressed SHA256 verified |

Files are in [the audit directory](../../artifacts/evaluation-audit-2026-11-10/).
Legacy Runaway/Reversal rows remain in compact-all and full archive; they are
not silently mapped into current groups. A valid current-group plan requires
0<StopLoss<EntryPrice<ExitPrice. Original order, all retained scalar strings
and row counts are checked against the source. The derivative views must not
replace the application report: they deliberately omit full scan-time evidence.

PatternVerdictReason values alone occupy34310969 bytes; indicator series occupy
73329426 bytes. Together93.2% of full file bytes, before attributing CSV quoting
and delimiters.12634 rows (86.8%) are Other, not published playable plans.
Diagnostics remain useful for missed-opportunity investigation and are archived.

## Results on recent current-group plans

Declared descriptive cohort: scan dates October30–November9 inclusive, to avoid
mixing the older deep-entry policy into the main summary. These51 plans all
carry EMA20 Bollinger metadata and StrategyVersion8. This does not establish
identical scanner rules: timing/native-H4 changes happened inside the period.

| Group | Plans | Entered | Win | Loss | Open | NoEntry | NoData | Mean winner profit |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| BellUp | 26 | 22 | 5 | 8 | 9 | 3 | 1 | +3.44% |
| ReversalHook | 25 | 21 | 5 | 6 | 10 | 4 | 0 | +6.34% |

Win/entered:5/22 and5/21. Win/resolved:5/13 (38.5%) and5/11 (45.5%).
Open and NoEntry are not negative training labels. Winner returns are computed
from saved exit/entry, before broker fees/slippage. Mean saved stop distances
for losses are-4.79% and-4.09%; they are not measured broker execution returns.
Different tickers, overlapping periods and unresolved plans preclude treating
these numbers as a portfolio backtest or independent strategy comparison.

Rank1 per saved group/scan in this cohort: BellUp3Win/2Loss/4Open across9
rank1 plans; ReversalHook2Win/2Loss/2Open across6. The dependable-rank1 goal
has not been achieved simply because some first-ranked plans won.

BellUp winners: CCL+1.50%,FSLR+2.13%,AHCO+6.03%,RUN+5.99%,WEN+1.54%.
Reversal winners: NFLX+5.01%,PINS+6.81%,NVTS+4.83%,SPCX+5.10%,RDW+9.96%.
These are saved evaluated outcomes, not verified broker fills.

For November9 alone: BellUp4 plans,3 entered (1Loss,2Open),1NoEntry;
ReversalHook13 plans,11 entered (3Win,8Open),2NoEntry. Morning rank1 NVTS
is Win+4.83%; evening rank1 BAC remains Open. BellUp rank1 VIR/QXO both
remain Open. Observation cutoffs vary by row; the latest evening plans have
not had the following full regular session by this report.

November4 reversals formerly reported4NoEntry/1Open now show4Loss/1Open:
AEVA,METC,SHOP,CRMD Loss; INTC Open. Waiting plans can enter later and then
lose: more entries alone are not evidence of better selection. The previous
[November5 audit](investigation-2026-11-05-reversal-noentry.md) remains a valid
historical observation of that earlier snapshot, not rewritten to today's state.

## Confidence warning

The BellUp cohort with saved model bellup-win-ridge-2026-10-05-v1, scope
WinGivenEntryAndResolution, EMA20 Bollinger model and StrategyVersion8 has
109 plans:101 entered (26Win,27Loss,48Open),7NoEntry,1NoData.
Among53 resolved entries the mean saved prediction is89.99%, whereas
observed Win/(Win+Loss) is49.06%. Brier loss is0.40868; an in-sample constant
observed-rate reference gives0.24991, descriptive only, not a fitted model
validated on held-out dates. In the90–100% bucket,32 resolved entries average
97.06% prediction but17/32 (53.13%) are Win.

Within the narrower October30+ cohort,13 resolved entries average83.25%
forecast versus5/13 (38.46%) Win. This is a small, unresolved/dependent sample,
but reinforces the need for policy-aware calibration. StrategyVersion is an
evaluation version, not a full scanner implementation version. No automatic
threshold/rank changes or retrospective forecasts were made from these results.
All model/scope/Bollinger/version groups and buckets are kept separate in JSON.

## Old unresolved results and retention

153 current-group Open rows are older than October11. Their latest EvaluatedAt
is October17, and all still have IsStaleOpen=false. They are historical,
right-censored observations rather than proof of still-active positions today.
The working view retains them explicitly; do not relabel them as Loss or
silently drop them from calibration denominators.

RecentScanDays is already60.2349 report rows lie before the November9-minus60
cutoff, and every one has a saved forecast. Current full and incremental builders
intentionally exempt saved forecasts from date/amplitude pruning for calibration.
Simply reducing RecentScanDays will not solve the combined diagnostic/series
size problem; deleting old forecast rows would damage the feedback population.
Full rebuild also reads evaluations and its archive, so manual trimming may be
undone on the next rebuild.

## Recommended production direction

Keep a compact everyday report with scalar outcomes, plan/provenance/confidence
and short diagnosis; keep full series and explanations in a separate keyed
detail archive. Maintain an independently readable all-date confidence history,
and carry unresolved plans irrespective of date. A recent detail window can be
30days as a display choice, not an evaluation-horizon change. Preserve exact
keys and original scanner evidence, including rejected Other, for investigations.

This requires coordinated reader/writer/merge/rebuild/export changes before
replacing the production CSV. Prepared views provide immediate smaller files
without making that migration implicitly. Reassess confidence by scanner-policy
dates and later held-out ticker/date groups before recalibration or retuning.

## Reproduction

[Offline script](../../../tools/analyze_report_20261110.py) reads local data and
creates the dated archive, three derivative CSVs and JSON using exclusive file
creation. Rerunning with existing outputs stops rather than overwriting them.
Only standard Python modules are used. Tools/artifacts follow existing ignored
directory policy; source files were not staged or committed.

Current contracts: [success criteria](../../guides/success-criteria.md),
[confidence history](../../project-skills/ibswingtrader-evaluation/references/CONFIDENCE_FEEDBACK.md).
