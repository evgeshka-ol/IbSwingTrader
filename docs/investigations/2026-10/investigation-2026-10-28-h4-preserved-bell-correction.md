# ARLO-like H4 correction with preserved Bollinger geometry

Offline cache investigation,2026-10-28. The user proposed looking for a
possible scan-time rationale in ARLO, rather than changing its Stalled
admission decision: H4 expanded upward around October23, then a sharp
October26 drop left a nearly horizontal mid and a still recognizable envelope.

## Artifacts and reproduction

- [Analysis script](../../../tools/analyze_h4_preserved_bell_correction.py).
- [All selected events, including comparison cases and missing outcomes](../../artifacts/analysis-2026-10-28-h4-preserved-bell-correction.csv).
- [Parameters, group summaries and selected-source hashes](../../artifacts/analysis-2026-10-28-h4-preserved-bell-correction.json).

Run the offline Python script from the repository root to regenerate from the
then-current mutable cache. It reads saved candles and writes research outputs;
it does not launch the C# application or request broker history.

## Prefix-only selection

Search July1 through October26,2026 inclusive. Calculate EMA20 with SMA20 seed,
population sigma20 and first-close MACD12/26/9 from the entire available native
H4 prefix. This is an independent float implementation of the documented
model, not a replay of the production Decimal BellUp classifier or proof of
Yahoo/SMA chart parity. Duplicate timestamps and malformed OHLC are excluded.

The numerical criteria describe the ARLO anchor and were specified before
examining the other candidates' subsequent outcomes. They are exploratory,
not empirically calibrated or independently validated thresholds:

1. Over the four H4 observations ending immediately before the red shock,
   upper gain>=1%, mid gain>=0.25%, width gain>=5%, with the latest width
   increasing and lower gain<=half the mid gain. Call this **Bell-like
   expansion**, not confirmed production BellUp.
2. A red shock body between-40% and-4%. The lower bound excludes the most
   extreme/cache-suspect discontinuities; corporate actions are not audited.
3. Select the first observation on that shock bar or the next two H4 bars
   (within32 elapsed hours) with close>=5, close<=mid, and absolute latest
   mid change<=0.25%. Observe only after that H4 bar has closed. An eight-bar
   ticker cooldown reduces repeated observations of one immediate correction.
4. PreservedShape requires mid and upper to remain within1% below their
   respective pre-shock values. ShapeRetreated uses the same prior shape,
   shock and flat-mid criteria but fails at least one retention boundary.
   This is a descriptive comparison, not matched or randomized controls.
5. Split the selected PreservedShape observations by whether the latest closed
   H4 MACD histogram strictly improves. This uses no later confirmation bar.

## Observation horizon and interpretation

Measure the **next regular trading session** high, low and close relative to
the selected H4 close. Prefer the native D1 bar; when absent, require all78
regular M5 bars from09:30 through15:55. Missing coverage is reported separately,
never counted as a loss. Next-session mapping excludes weekends and the
July3/September7 exchange holidays in this study's fixed2026 interval.

These are price excursions, not Win/Loss or achievable profits. A gap can
remove the apparent entry opportunity; intraday high/low ordering and execution
costs are unknown. The remainder of the signal day and its after-hours are
outside this next-session outcome metric. Native H4 close differs from a later
saved scan quote. Cache existence does not prove timely delivery during a scan.

ARLO October26 demonstrates the reference issue: H4 closes at12.38; the next
session reaches13.50, or9.05%. The actual evening scan reference12.01 gives
12.41%. The two measurements are compatible but must not be mixed.

The cache universe and coverage are selected by prior application activity,
and repeated ticker events are dependent. This study cannot establish a
tradable edge, improve the Win confidence model or justify an admission change
from a favorable maximum alone. Current project success remains entered-plan
Win/profit/confidence, with amplitude supporting diagnosis.

## Results

Read1,817 JSON files:1,816 market caches and one service cache;1,775 had
enough H4 history. No market-file processing errors. Selected498 events across
383 tickers, comprising472 PreservedShape events across368 tickers and26
ShapeRetreated events. Five PreservedShape outcomes lack complete coverage.

| Next-session measure | PreservedShape | ShapeRetreated |
| --- | ---: | ---: |
| Observed events | 467 | 26 |
| Median high / reference | +2.67% | +2.55% |
| Median low / reference | -2.38% | -3.63% |
| Median close / reference | -0.08% | -1.18% |
| High at least+10% | 39/467 (8.35%) | 1/26 (3.85%) |
| Low at most-5% | 111/467 (23.77%) | 6/26 (23.08%) |
| Positive close / reference | 226/467 (48.39%) | 9/26 (34.62%) |

PreservedShape events by month: July175, August129, September103, October65.
Of467 observed outcomes,466 use native D1 and ARLO October26 uses the complete
next-session M5 fallback. Favorable maxima and adverse lows may coexist; these
counts are not wins versus losses. The small comparison group and unadjusted
repeated ticker dependence do not establish predictive lift.

All472 PreservedShape observations still had a weakening closed H4 histogram
at their initial qualifying point. The planned same-time momentum split is
therefore empty on its improving side. This study does not test whether waiting
for a later MACD recovery yields a useful entry, nor claim such a filter works.

## Concrete chart-review examples

Times below identify the native H4 candle START in New York; features become
observable only four hours later. Percentages use its CLOSE and the next regular
session. Examples illustrate both directions and are not an outcome-ranked
validation sample.

| Ticker | H4 candle start | Reference | Next high | Next low | Next close |
| --- | --- | ---: | ---: | ---: | ---: |
| ARLO | 2026-10-26 12:00 | 12.380 | +9.05% | +2.91% | +5.41% |
| NE | 2026-10-22 08:00 | 42.530 | +12.44% | +3.15% | +12.16% |
| INIO | 2026-10-05 12:00 | 18.970 | +12.86% | +1.42% | +12.55% |
| VCYT | 2026-10-06 08:00 | 45.360 | -2.54% | -6.66% | -4.85% |
| ACHV | 2026-10-01 08:00 | 7.745 | -1.48% | -8.97% | -6.52% |
| ARLO | 2026-08-07 08:00 | 15.350 | +0.07% | -10.75% | -4.04% |

ARLO's August7 analogue is especially useful: almost the same flat-mid/retained-
envelope idea on the same ticker preceded a next-session low-10.75% and close
-4.04%, with the high only+0.07%. VCYT October6 and ACHV October1 also failed.
Among October PreservedShape cases other than ARLO with observed outcomes,
VCYT and ACHV are the two closest to the anchor under the prefix-only distance
`sum(abs(feature-anchor)/scale)` using shock body(scale4), mid retreat(1), upper
retreat(1), latest mid delta(0.25), prior mid gain(1), prior width gain(30).
No future price is used by that distance. NE and INIO are illustrative recent
strong continuations, not nearest neighbors chosen by this distance.

## Implication

The cache contains many examples, including favorable continuations, but the
initial flat-mid/retained-envelope configuration alone has weak descriptive
separation: median next-session high about2.67% and close near zero. Keep the
result as a chart-review hypothesis. A separate, time-correct investigation
could define a later confirmation point, reprice entry there and inspect both
remaining upside and downside. No live scanner, ranking or confidence behavior
was changed by this study.
