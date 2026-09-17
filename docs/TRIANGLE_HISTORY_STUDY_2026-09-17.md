# Post-rise consolidation and oval-envelope cache study

## Scope

Read-only historical study using `tools/analyze_triangle_history.py`. No
scanner admission or ranking changes. No broker requests or application/test
runs. BTC/FDUSD is a visual motivation only, not a numerical reference label.

Available usable series: H4, 1,677 tickers / 1,462,455 bars; Daily, 1,383
tickers / 259,453 bars. H4 spans 2023-07-03 through 2026-09-16; Daily spans
2025-10-14 through 2026-09-16. Coverage differs substantially across symbols.
Files without timeframe objects are skipped. Nonpositive OHLC rows are
excluded; timestamps are sorted/deduplicated; holes longer than seven days
split the history. Today's potentially incomplete bars are excluded.

## Exploratory Definition

- Upward movement over 1, 3 or 6 bars: at least 5% and 0.75 times the initial
  Bollinger width, with net gain at least 75% of summed positive close changes.
- Subsequent plateau of 8, 12, 16 or 20 bars. Its median body is small relative
  to the mean body of the rise; its close range, distance from the rise's final
  close, and drift between first/last three closes are bounded by the rise.
- Oval subset: Bollinger width first expands at least 1.3x and subsequently
  contracts; upper ends lower and lower ends higher than at maximum width.
  At least three bars follow the width peak. Net contraction / total absolute
  width movement after the peak must be at least 0.65, limiting re-expansion.
- Bollinger uses 20 closes, population standard deviation, multiplier 2,
  matching the application's basic formula. Calculations use Python floats.

Strict/base/loose thresholds respectively: plateau range and distance
0.30/0.45/0.60 of rise; body ratio 0.25/0.40/0.55; drift 0.15/0.25/0.35
of rise; final/peak width at most 0.70/0.80/0.90. These are illustrative
search tolerances, not fitted or validated classifier thresholds.

Overlapping qualifying windows within each symbol/timeframe/profile/stage
are merged into episodes; the first detection represents an episode. Counts
are not independent trades. Cross-ticker market correlation is not removed.

## Counts

| Profile | H4 plateau episodes | H4 oval episodes (tickers) | Daily plateau episodes | Daily oval episodes (tickers) |
| --- | ---: | ---: | ---: | ---: |
| Strict | 1,575 | 237 (213) | 80 | 15 (15) |
| Base | 5,029 | 1,278 (860) | 480 | 86 (85) |
| Loose | 8,712 | 2,952 (1,320) | 1,233 | 302 (278) |

Frequency is highly tolerance-dependent. Even the strict search finds
examples on both timeframes, but these are shape candidates, not manually
confirmed Triangle labels. Only 9 of 1,364 base oval episodes have fewer than
75% positive-volume plateau bars; this does not establish adequate liquidity.
Multi-bar window selection does not by itself prove multiple strong candles:
one large jump can dominate such a window.

## Examples Checked Against Cached OHLC

- **SMMT H4:** 2026-09-02 04:00 to 09-03 12:00, closes rise from 14.10
  to 17.11 through several steps. Next 16 closes lie between 17.00 and 17.85;
  median body / mean rise body is 0.293; band width falls 55.5% from its peak
  by 09-10 12:00. A useful example of a non-flat plateau with excursions.
- **AUR H4:** same rise interval, 5.48 to 6.315, followed by 16 bars with
  closes 6.26-6.54. Body ratio 0.327; width falls 49.5% by 09-10 12:00.
- **NMAX Daily:** 08-10 to 08-18, 8.77 to 11.43, followed by 16 daily
  closes 10.39-11.00. Body ratio 0.302; width falls 71.4% by 09-10. The
  plateau is below the rise's final close, not a perfectly level continuation.
- **ACVA H4:** recent September episode matches the plateau stage, not the
  closed-oval stage in available H4 history (through 09-15). Do not infer a
  completed oval from the later Yahoo screenshot in the cache study.
- **TE H4:** 05-22 12:00 to 05-29 04:00 matches the base plateau stage
  but not oval. Width remains about 90.5% of peak and contraction smoothness
  is only 0.439. This demonstrates the danger of rejecting every post-rise
  pause; it is not a full validation against the supplied TE chart/timeframe.

## Conclusions and Limits

Related structures exist frequently enough to assemble a labelled library
now. The envelope stage filters many candle-only matches. However, a finished
oval is a retrospective description: each window here uses only bars through
its endpoint, which can be well after a historical scan. No claim is made
about early detectability, profit, future continuation, or precision/recall.
Future candles after the endpoint are not consulted.

Next useful step is visual labelling of selected cache-derived examples and
BellUp counterexamples, followed by scan-time replay. Do not immediately turn
this broad shape search into a production veto. Current detector thresholds
and behavior are unchanged.

Detailed base-profile CSVs and summary are generated under
`/tmp/triangle-history/` by the analysis script. No source datasets are edited.
