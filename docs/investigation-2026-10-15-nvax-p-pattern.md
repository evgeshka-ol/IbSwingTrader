# NVAX H4: П-Pattern and BellUp readiness warning

The user identifies NVAX on October 5-6 as П-Pattern: one sharp rising
candle, a short sideways bridge and a sharp falling candle. PENG's Л differs
by a gradual rising leg. Chart: `Data/Charts/NVAX-H4.png`. User chart labels
are October 5 13:00 rise, October 6 04:00 upper-band bend and 08:00 fall.

Read-only native H4 cache inspection using SMA20 and population deviation x2:

| Native start | Close | Upper | Mid | Upper increment |
| --- | ---: | ---: | ---: | ---: |
| October 5 08:00 | 10.82 | 11.04938 | 10.7230 | -0.02039 |
| October 5 12:00 | 12.56 | 11.67612 | 10.8105 | 0.62674 |
| October 5 16:00 | 12.45 | 12.01106 | 10.8880 | 0.33494 |
| October 6 04:00 | 12.43 | 12.27346 | 10.9645 | 0.26240 |
| October 6 08:00 | 11.29 | 12.30186 | 11.0045 | 0.02840 |

The pre-fall native series confirms two successive reductions of upward
speed and stalled price above a rising mid. The near-flat upper band appears
on the falling 08:00 candle. Completed 04:00 geometry is usable from 08:00.
This later cache does not prove broker availability at a historical scan or
full Yahoo/native equivalence. No final falling candle is a pre-fall feature.

The user reports NVAX was the only October 14 scan winner, from min/max
fluctuations rather than the expected impulse. This is user feedback, not an
independently verified aggregate or proof that the evaluated Win is invalid.
October 5-6 geometry does not itself reject NVAX on October 14: its current
prefix must satisfy the guard. An old episode is not a permanent veto.

The user authorized a readiness filter based on PENG and NVAX. Conditions
are canonical in SERIES_PLAYBOOK.md "П-Pattern". The rule uses two successive
upper-increment reductions, a preceding price rise and a nonadvancing latest
close above flat/rising mid. No fitted magnitude cutoff or full Л/П detector
is claimed. Successful continuation pauses can be rejected; independent
outcome validation remains needed. Earlier standalone-kink studies remain
relevant limitations.

Added checks for both examples, counterexamples, scale invariance, invalid
inputs and completion boundaries. Builds, tests, application and broker
requests were not run. Historical candidate/evaluation rows were not changed.

## Follow-up: TWS chart supplied by the user

`Data/Charts/NVAX-H4-TWS.png` provides an exact candle anchor: its selected
October 5 19:00 candle displays H13.39/L10.73/C12.56. These match the native
cache October 5 12:00 candle exactly (O10.81/H13.39/L10.73/C12.56).
This establishes a seven-hour label difference for this particular candle,
not a different underlying impulse. The chart timezone is not shown; do not
infer a universal timezone conversion or session-grid rule from one match.

The TWS legend explicitly shows `BOL(20, 2.0 - EMA)` and `EMA(20)`.
The scanner FeatureEngine uses a twenty-close arithmetic mean and population
standard deviation. Thus a broker chart and broker OHLC can agree while their
Bollinger curves differ. The screenshot supports the user's visible П shape,
but is not numerical confirmation of the scanner's exact upper-band kink.
The exact TWS band-deviation convention is not established by the screenshot.

For direct visual comparison use matching Bollinger SMA20/x2 settings and
align candle OHLC/timestamps, or obtain the actual TWS indicator values at
successive points. No switch of scanner indicators to EMA was made. The
experimental combined guard remains a slowdown/stalled-price proxy, not
proof of detecting the same visually sharp EMA bend.

## Follow-up decision: collect pause diagnostics, withdraw the veto

After comparing the SMA chart, the user clarified that the sharp kink is
seen on later data; preceding candles show a rise and a short pause whose
direction is unresolved. It could precede a Triangle-like consolidation,
another rise, or a fall. The user explicitly requested diagnostic-only
collection instead of filtering.

The readiness veto above was removed. The same geometry is now saved as
`BellUpPauseDiagnostic(DiagnosticOnly=True, ...)` after admission/ranking/
publication, in candidate reasons, context notes and logs. It does not
change groups, trade levels, rank or Win~ and makes no broker requests.
Original decision and completed-bar timestamps and exact deltas are saved
to support later research. No claim of predicting a Л/П fall remains.
Earlier sections retain the initial investigation/implementation history.
