# Saved BellUp confidence: descriptive calibration audit

Recorded 2026-10-21 from evaluation-dataset.csv and BellUpWinProbability.cs.
Read-only CSV analysis; no application, broker, build, test, refit or historical
outcome replay was run. Original forecasts/outcomes were not changed.

## Model and population

Model bellup-win-ridge-2026-10-05-v1, scope WinGivenEntryAndResolution.
The code describes46 resolved replay plans/28 training tickers, ticker-weighted
ridge logistic regression, not holdout calibrated. Inputs are planned profit,
planned stop distance, VolumeRatio20 and H4 RSI change. It does not directly
encode Bollinger curve geometry or a time-to-target prediction. Profit has a
negative coefficient; stop distance, volume and RSI change positive coefficients.
That is the fitted formula, not evidence that widening a stop improves a trade.

169 saved compatible forecasts:44 Win,45 Loss,55 Open,25 NoEntry.
Only89 entered resolved rows can contribute ordinary binary Brier loss.
Keep indicator-model cohorts separate; do not treat blank historical
BollingerModel as a known current EMA identifier.

## Legacy/unspecified Bollinger cohort

81 entered resolved rows,58 tickers, scans October6–16. These saved outcomes
were not revalidated (including previously noted historical evaluation issues).
This is an observational audit, not an independent chronological holdout.

| Forecast bucket | Resolved | Win | Loss | Mean forecast | Observed Win share |
|---|---:|---:|---:|---:|---:|
| 50–<60% | 10 | 4 | 6 | 57.13% | 40.00% |
| 60–<70% | 15 | 5 | 10 | 64.13% | 33.33% |
| 70–<80% | 33 | 15 | 18 | 74.48% | 45.45% |
| 80–100% | 23 | 12 | 11 | 86.01% | 52.17% |

Overall mean forecast73.70%, observed Win36/81=44.44%, mean Brier0.32649.
The saved percentages are substantially overconfident in this population.
Small buckets, repeated ticker/scan dependence and possible training overlap
prevent treating these frequencies as a validated replacement calibration map.

## Current EMA cohort

47 forecasts with BollingerModel ema20-sma-seed20-popstd20-v1, scans October19–20:
8 Win,31 Open,8 NoEntry, no resolved Loss yet. Eight resolved winners alone
cannot establish calibration: the much larger unresolved population has had
less time to finish, and quick resolutions select easier/earlier targets.
Do not report100% success or label pending rows as losses.

Examples:

- XERS October19 evening:98.2%, Open, provisional target progress35.71%.
- SPT October20 evening:98.6%, Open, provisional progress19.51%.
- HTFL October20 evening:98.3%, NoEntry; no binary outcome score.
- TREX October20 morning:82.0%, Win, progress100%.

These progress values are not confidence observations or fractional wins.
The forecast concerns Win conditional on entry and resolution, not tomorrow's
profit or reaching a target within a stated short horizon. That limitation
does not excuse the measured overconfidence in the older resolved cohort.

## Improvement requirements

Preserve the original forecast and model/scope labels. Historical feedback
refresh measures error; it does not train or calibrate the live predictor.
Calibrate/refit on earlier resolved, coverage-audited plans and evaluate on
later untouched dates, keeping overlapping ticker episodes together. Separate
legacy and EMA/plan-policy cohorts. Benchmark calibration, Brier and rank-1
profit together; raw ordering by the old percentage is not validated ordering
by near-term opportunity or expected profit.

A probability with a deadline is a different target. A future H4/D1-specific
time-to-target model needs an explicit horizon and consistent censoring of
unfinished observations; silently relabeling existing Open as Loss would be
incorrect. No horizon, model coefficient, probability display or sort policy
was changed by this investigation.
