# EstimatedHitRatePct comparison — 2026-10-05

Source: saved candidates joined to evaluation dataset by ticker and ScanTime.
Scope: 70 actual BellUp candidate snapshots, 2026-09-23 through 2026-10-02;
68 have DiagnosticsEstimatedHitRatePct. Outcomes are from the available
report, before the new target-reduction rule is used in future scans.

## What the number means in code

CandidateFinder.EstimateHitRatePct maps the TodayResearchLike quality score
to three fixed buckets: score >=25 =>85%, >=10 =>55%, otherwise30%.
These are historical estimates of AmplitudePct>=10%, calibrated under the
legacy Runaway setup on 2026-09-01, not probabilities of a profitable
trade. The code explicitly calls them rough, not statistically calibrated.
The legacy alternative family uses 50%/30% and is not analysed here.
ReRankCandidates skips assigning diagnostics when the pool has <=1 row;
two current BellUp snapshots have no estimate.

## Observed comparison

| Estimated % | All plans | Win | Loss | Open | NoEntry | Win/(Win+Loss) | AmplitudePct>=10% |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 30 | 32 | 9 | 5 | 17 | 1 | 64.3% (14 closed) | 8/32 =25.0% |
| 55 | 23 | 9 | 6 | 8 | 0 | 60.0% (15 closed) | 8/23 =34.8% |
| 85 | 13 | 4 | 2 | 6 | 1 | 66.7% (6 closed) | 4/13 =30.8% |

Missing estimates: one Win and one NoEntry. They are excluded from buckets.
Signed AmplitudePct is used as stored, without converting a downside move
to an upside success. The positive MaxPct>=10% counts are respectively
5/32, 5/23, 3/13; this is a different target from AmplitudePct>=10%.

## Friday snapshots

| Ticker | Estimate | Quality score | Outcome | AmplitudePct |
|---|---:|---:|---|---:|
| DNA | 55% | 24.30 | Loss | -12.23 |
| VSH | 55% | 20.24 | Win | 11.86 |
| WOLF | 55% | 18.15 | Win | 15.58 |
| AXTI | 55% | 13.10 | Open | 8.48 |
| XXI | 30% | 2.82 | Open | -7.15 |
| MRAM | 30% | -7.93 | Win | 5.44 |

## Interpretation and limits

Current observations do not demonstrate useful separation of trade wins by
these percentages. The 85% bucket also falls well below its nominal rate
on the intended amplitude target so far. This is provisional, not a final
ten-day calibration: many horizons are unfinished, closed-only win rates
are biased toward trades resolving earlier, samples are small, and repeated
tickers across scans are not independent. Scanner and exit rules have
changed since the original calibration. No probability/score thresholds
were altered based on this sample.

Treat this field as a coarse historical quality indicator, not a calibrated
probability of profit. Validating a win probability requires a separately
defined target tied to the current entry/exit/stop policy and completed
evaluation horizons, followed by validation on later scans.
