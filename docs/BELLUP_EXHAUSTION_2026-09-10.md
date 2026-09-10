# BellUp exhaustion investigation, 2026-09-10

## Evaluation of September 9 scans

The last scan (09:49:58 exchange time) admitted nine Runaway rows:

| Rank | Ticker | Outcome so far | AmplitudePct |
| --- | --- | --- | --- |
| 1 | ALM | Open | 4.46 |
| 2 | SMR | Open | 2.99 |
| 3 | SMTC | NoEntry | 5.32 |
| 4 | SECZ | Open | 5.96 |
| 5 | INTC | NoEntry | 2.62 |
| 6 | ZIM | Open | 3.10 |
| 7 | MARA | Open | 3.45 |
| 8 | ORCL | Open | 2.64 |
| 9 | SPCX | Open | 4.78 |

None reached 10% amplitude in the available evaluation window. Across all
three scans there are 24 admitted Runaway rows, no Wins, two Losses, four
NoEntries and 18 Open outcomes. These are not 24 independent ideas. Open is
not a final loss. Scanner quality in this window is weak, independently of
trade-plan conversion. Changing universes, scan times and builds prevents
attributing differences between these scans solely to code changes.

## SECZ: what was observable at the scan

The refreshed `Data/Charts/SECZ-H4-1.png` shows a plateau beginning at the
September 9 04:00 candle, followed by several sideways candles. However,
the 09:49:58 candidate snapshot ends at that first completed 04:00 candle.
The subsequent plateau was not yet available to this scan. The current H4
cache also ends at that candle; do not manufacture the later H4 history
from the screenshot or use future completed candles as scan-time features.

The saved scanner values already show an early warning:

- Close: 8.29 -> 8.40 -> 8.25; latest body 8.64 -> 8.25.
- Upper band: 8.11 -> 8.35 -> 8.51; positive increments slow from 0.24 to 0.16.
- Lower band: 5.74 -> 5.70 -> 5.71; first upward turn.
- MACD histogram: 0.15 -> 0.16 -> 0.15.
- RSI: 100 -> 100 -> 92.72 (scanner values, not Yahoo's indicator values).

This is a possible transition out of expansion, not yet proof of a lasting
plateau. No warning based on the completed 04:00 candle can be claimed
before its 08:00 close. The first scan at 06:21 could not know it; the
08:25 snapshot still ends at the previous day's 16:00 candle.

`CandidateFinder.IsLateBellUpPhase` applies only to Daily confirmations.
`IsH4BellUpTerminalPullback` requires the red body to return toward the
middle of the upper half-band: latest close <= mid + 0.5 * (upper - mid).
For this SECZ snapshot that level is 7.81, well below its 8.25 close.
It therefore misses exhaustion near the upper band. The two-candle green
body boost veto is a separate question and does not detect exhaustion.

## Reproducible feature screen

Run `python3 tools/analyze_bellup_exhaustion.py` for an offline CSV-only
analysis. It does not start the application or modify data/settings.
Candidate snapshots supply all features; evaluations supply only outcome
and amplitude. Select admitted Runaway from candidate metadata, retain the
latest evaluated snapshot per ticker/day, exclude NoData. No future pattern
verdict is used for selection. This includes both H4 and Daily confirmations;
the CSV lacks the original confirmed-timeframe field.

Let U/L be H4 upper/lower bands, H the MACD histogram, C the close, and
W = U[t-1] - L[t-1]. Larger values hypothesize exhaustion:

- Upper deceleration = ((U[t-1]-U[t-2]) - (U[t]-U[t-1])) / W.
- Lower rise = (L[t]-L[t-1]) / W.
- Histogram decay = (H[t-1]-H[t]) / W.
- Close decline = (C[t-1]-C[t]) / W.
- Low directional efficiency = 1 - abs(C[t]-C[t-3]) / sum(abs(delta C))
  over those three transitions; 1 when all closes are equal.
- Geometry-and-momentum flag: first three features strictly positive.
- Price-confirmed flag: the above flag and close decline strictly positive.

The target here is amplitude <= 10%, not a manually labeled exhausted
pattern. AUC 0.5 means no separation; these are exploratory results, not a
validated exhaustion classifier.

| Feature | Rows | AUC low amplitude | Before Sep 8 | Sep 8 onward |
| --- | --- | --- | --- | --- |
| Upper deceleration | 311 | 0.544 | 0.532 | 0.778 |
| Lower rise | 311 | 0.612 | 0.611 | 0.667 |
| Histogram decay | 311 | 0.602 | 0.591 | 0.546 |
| Geometry-and-momentum flag | 311 | 0.505 | 0.492 | 0.509 |
| Close decline | 82 | 0.757 | 0.747 | 0.685 |
| Low directional efficiency | 82 | 0.702 | 0.714 | 0.593 |
| Price-confirmed flag | 82 | 0.554 | 0.543 | 0.472 |

The geometry flag catches the September 9 SECZ and preserves its September
8 winning snapshot, but flags 69 historical rows, including 12 with >10%
amplitude and three Wins. The price-confirmed flag flags 17 of 82, including
one high-amplitude row; its later-period separation is worse than chance.
Neither conjunction justifies a hard veto.

Only nine of the 82 raw-close rows have >10% amplitude; the later slice has
only two such rows among 29. The chronological split is a sensitivity check,
not an untouched holdout: SECZ motivated the hypotheses. Repeated tickers
across dates, rounded indicator series, selection by existing admission
and different evaluation horizons remain limitations. The SECZ September 8
amplitude is now 14.18 in the updated report; earlier reports had 13.52.

## Decision and next step

No production filter, score or trade-plan change is justified by this
screen. Preserve separate notions of BellUp geometry and its current phase.
The proposed phase model is: expansion, possible exhaustion, confirmed
plateau, and renewed expansion. It is not yet implemented or calibrated.

A plateau candidate should combine a preceding expansion with low net
price progress over multiple completed bars, overlapping price ranges and
weakening band expansion; MACD/RSI are supporting evidence. A single red
bar is an early warning, not confirmed exhaustion. A new breakout must be
able to restore the active state, rather than permanently excluding a
ticker because an old boost happened.

Before wiring this into admission/ranking: label both genuine plateaus and
successful continuation pauses at their observable timestamps, retain
bar timestamps and confirmed timeframe in snapshots, validate price-based
features on more high-amplitude examples, then evaluate the fixed rule on
new scan dates. Do not relabel the future plateau as known at its first bar.

## Follow-up: sequential phase prototype on existing data

Implemented an offline hypothesis model in
`tools/analyze_bellup_phases.py`. Run it with Python from any directory.
Production scanner behavior remains unchanged. Labels are algorithmic
hypotheses, not independently expert-labeled BellUp phases.

The fixed initial window is three H4 transitions, with a 0.25 progress
ratio. These are exploratory definitions, not optimized trading thresholds:

- Expansion: close, upper band and band width all increase on the latest bar.
- Possible exhaustion: after expansion, close and MACD histogram decline
  while the upper-band increment decelerates. The warning persists until
  expansion resumes or a plateau forms.
- Plateau: the preceding three transitions had positive net close progress
  and increasing upper band/width; the latest three make at most 25% of
  that price progress in absolute terms, their candle ranges overlap
  pairwise, and upper-band growth slows versus the preceding window.
- Freeze the plateau's high/low box when it forms. A close above that box
  with expanding bands is renewed expansion; a close below it is breakdown.
  An upside exit without band confirmation is unclassified, not plateau.
- Other situations are unclassified; they are not assumed playable.

Process the saved series left to right. No outcome or later candle enters
the state calculation. The finite snapshot history can truncate an earlier
phase, and missing bar timestamps prevent a general audit of completion
times. Indicator arrays must have the same length as OHLC arrays; missing
history is excluded, not filled from today's cache.

### Primary cohort: admitted Runaway

82 usable ticker/day snapshots on six scan dates (September 1-9), out of
311 joined admitted ticker/days. Latest evaluated admitted snapshot per
ticker/day retained. Only nine have amplitude >10%, and only two are Wins.

| State at snapshot end | Rows | Amplitude >10% | Wins | Median amplitude |
| --- | --- | --- | --- | --- |
| Expansion | 33 | 6 | 2 | 4.12 |
| Possible exhaustion | 18 | 2 | 0 | 4.79 |
| Plateau | 9 | 0 | 0 | 3.37 |
| Breakdown | 1 | 0 | 0 | 2.48 |
| Unclassified | 21 | 1 | 0 | 4.46 |

SECZ September 8 is Expansion; September 9 is PossibleExhaustion, not
Plateau. Both available Wins (SECZ and BE) remain Expansion. A hard warning
veto would also remove two high-amplitude cases: ASST (11.50%, NoEntry) and
CIFR (11.98%, Loss). Do not equate amplitude with profitable upward movement.

Plateau examples: OCTV, VRNS, SMR, CNH, FUBO, HOOD, BLSH, ORCL, ZIM.
Before September 8: five plateaus, none high-amplitude; September 8 onward:
four, none high-amplitude. Even under independent random sampling, drawing
zero high-amplitude rows in nine from this imbalanced population has about
33% probability. This is not compelling evidence by itself, and repeated
tickers/date effects further weaken independence.

Sensitivity check, not threshold selection: windows 2/3/4 and progress
ratios 0.15/0.25/0.40 produce 4-32 plateau rows, none high-amplitude in this
small admitted cohort. Membership changes substantially. The originally
specified three-bar/0.25 setting is retained only for reporting.

There are no RenewedExpansion states at the end of these 82 snapshots.
Seventeen snapshots contain a renewal somewhere earlier in their history,
but their eventual scan evaluation cannot label those earlier events.
They may also describe duplicate historical episodes.

### Secondary cohort: all candidate groups

To look for counterexamples beyond the admitted selection, repeat the same
fixed model on all candidate groups, keeping the latest evaluated snapshot
per ticker/day. This is a separate population, not a BellUp-specific test.
1069 rows have aligned series, of which 111 (10.38%) have amplitude >10%.

| State | Rows | Amplitude >10% |
| --- | --- | --- |
| Expansion | 199 | 28 |
| Possible exhaustion | 286 | 24 |
| Plateau | 52 | 5 |
| Renewed expansion | 2 | 1 |
| Breakdown | 4 | 0 |
| Unclassified | 526 | 53 |

The plateau high-amplitude rate is 9.62%, close to the 10.38% population
baseline. Counterexamples include PSQL on September 1 (37.48%) and 2
(21.24%), CNXC September 8 (13.01%), CHPT September 8 (12.28%), and CHYM
September 8 (16.57%). PSQL's two rows are not independent. Many Other rows
have zero trade plans, so their NoEntry labels cannot measure conversion.
Only two endpoint renewals exist, insufficient to validate that state.

### Conclusion

Existing data supports implementing and inspecting a sequential phase
hypothesis, but does not yet support using it as a validated exhaustion
veto or ranking weight. Plateau is not equivalent to exhausted upside.
The final September 9 top-1 ALM remains Unclassified; these rules do not
solve top-1 quality, and no ranking improvement is claimed.

The offline analysis was executed. Application builds, scanner runs and
project tests were not launched. Next evidence should distinguish genuine
exhaustion from successful pauses using timestamped phase labels and a
fixed future horizon; widening a veto based only on SECZ is not warranted.
