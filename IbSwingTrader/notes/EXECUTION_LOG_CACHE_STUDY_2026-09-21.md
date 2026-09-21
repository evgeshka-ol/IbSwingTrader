# Scanner-log and cached-M5 replay

Read-only study executed on September 21 with
`python3 tools/analyze_execution_logs.py --output /tmp/ibswingtrader-execution-log-study`.
No broker calls, application/test runs, historical dataset edits or trading-rule changes.

## Selection and timestamps

136 files contain `Scanner request:`; 83 are evaluation-only logs and 23
other files. There are 122 completed scanner runs and 14 unfinished ones.
Candidate rows must be admitted Runaway (not Other/DiagnosticRejected),
match exactly one completed run by timestamp and ticker/preset processing,
and have BellUp evidence from that run's promotion log or saved candidate
verdict. Evaluation outcomes are not used to select candidates.

Of 3,072 candidate rows, 1,137 have no matching log, 1,012 lack saved/logged
BellUp evidence and two duplicate ticker/scans are excluded. The resulting
921 snapshots cover 91 completed scans. Keeping the latest snapshot per
ticker/day, before examining cache coverage or outcomes, leaves 700 cases.

First `Processing ticker` is an observable processing time, not the exact
broker discovery or quote timestamp. The selected preset's processing time
is retained separately. Completion is the reference time when exact row
publication is unavailable; legacy console output may precede completion.
Actual run delays are used, not a fixed maximum added to CSV ScanTime.

## Delay and coverage

For 700 ticker-days, first processing to completion has median 27.78 minutes,
90th percentile 54.87 minutes and maximum 97.00 minutes. The latter is SGML
on August 25, 04:30:38 to 06:07:38. This is the BellUp cohort, not the maximum
over every ticker in all scanner logs.

143 have both adjacent M5 bars around completion: 96 have a nonpositive
preceding body and 47 have a positive body. Of the 557 missing pairs, 463
are missing the preceding bar and 94 lack both. Spot checks confirm cases
where cache coverage begins with the bar containing scan completion, so
the previous body cannot be reconstructed from that cache.

## Fixed forecast and results

Prediction = current M5 open + previous completed M5 close - previous M5 open,
only when the previous body is positive. Features never use current final
close/high/low. The future label is the current M5's final close. Baseline:
the current open with no added body. Errors are absolute percentages of
current open. Historical arithmetic uses unrounded Python floating values.

| Cohort | Forecasts | Formula MAE | Open baseline MAE | Formula better / tied |
| --- | ---: | ---: | ---: | ---: |
| All snapshots (includes rescans) | 92 | 0.503% | 0.373% | 31 / 1 |
| Latest ticker/day | 47 | 0.508% | 0.326% | 13 / 1 |
| Latest ticker/day, positive volume in both bars | 42 | 0.549% | 0.365% | 13 / 1 |

The 47-case sample spans 40 tickers on 16 dates. September alone contributes
18 forecasts: 0.478% versus 0.335% baseline MAE, four better forecasts.
June and August also have higher formula MAE; July has no eligible pair.
These are exploratory period checks, not an untouched holdout.

The simple body continuation has not improved current-M5-close prediction
in this selected history. This does not establish an optimal entry price,
fill probability or trade profitability, and does not invalidate BellUp on
H4. A proposed minimum acceptable entry is a different target from point
forecast error at M5 close. Formula remains diagnostic only.

## MSTR September 21

First and selected HOT_BY_VOLUME processing: 04:19:18.986218. First
phase-ready plan: 04:58:47.125045. Completion: 05:03:21.318804.
Delays: 44m02s from processing, 4m34s from first plan. CSV ScanPrice 162.49,
EntryPrice 153.91. The needed 04:55 and 05:00 M5 bars are absent from the
local cache, so the chart-provided 163.18 example is not claimed as a
cache-verified forecast.

## Limits and artifacts

Old code versions, repeated tickers, shared market moves, incomplete caches,
zero-volume bars and possible vendor revisions constrain interpretation.
Cached bar open assumes trading had begun by the decision time; exact first
tick availability and partial prices cannot be recovered from these logs.
H1 aggregation cannot manufacture missing M5 bars or their ordering.

Detailed joined rows: `/tmp/ibswingtrader-execution-log-study/replay.csv`.
Summary: `/tmp/ibswingtrader-execution-log-study/summary.json`.
The script can regenerate both; temporary artifacts are not source datasets.
