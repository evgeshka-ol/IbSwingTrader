# BellUp to convergence: forward-shape investigation

## Question and method

User hypothesis: a lower-band upward turn precedes the transition from an
expanding BellUp to a sideways price ceiling approached by a rising middle
band. Later upper-band deceleration confirms the transition, but cannot be
an input available at the earlier decision time.

`tools/analyze_bellup_transition.py` implements an offline exploratory study.
No scanner filter, score or trade plan was changed. No builds or project
tests were run. The analysis script and SECZ reconstruction were executed.

Use aligned original H4 OHLC/Bollinger series from candidates.csv, including
Other candidates. At each historical decision bar t, features use only the
prefix ending at t; the next three H4 bars supply the shape label. Evaluation
Win/Loss and amplitude are not labels in this experiment.

Cache H4 OHLC supplies timestamps only: require an unambiguous match of three
consecutive rounded OHLC candles and completion before the source snapshot.
Ambiguous/unmatched windows are excluded (27). Retain the earliest source
snapshot for each ticker/timestamp, then greedily retain non-overlapping
forward windows per ticker, without looking at labels. Historical feature
windows can still overlap, and tickers are not independent.

Eligibility is a preceding expansion proxy, not the production BellUp
classifier: prior three-transition close gain G >= 20% of the prior band
width, rising middle band, and growing width. This yielded 1659 unique
eligible events and 779 after forward-window thinning.

The fixed three-bar future labels are:

- Convergence: close range including t <= 0.5G, net close movement <= 0.25G
  in absolute value, middle band rises, price stays above it at the endpoint
  with a smaller price-minus-middle gap, and upper-band growth over the
  future window is smaller than over the preceding three transitions.
- Continuation: final close exceeds the preceding four-bar high and gains
  more than 0.5G from the decision close.
- Other: neither. Do not silently label these cases failed continuations.

The fractions are initial hypothesis definitions, not optimized thresholds.
An initial label omitted the positive, shrinking endpoint gap; this was
corrected to match the user's geometry. Reported results use the corrected
label. Thus the chronological split is exploratory, not an untouched holdout.

## Results

779 episodes: 110 convergence, 146 continuation, 523 other. AUC below is
computed only on the 256 convergence/continuation episodes; higher values
predict convergence. The large Other group limits operational conclusions.
Continuous features are normalized by the preceding Bollinger width.

| Feature at decision bar | All | Before September 1 | September 1 onward |
| --- | --- | --- | --- |
| Lower-band increment | 0.503 | 0.531 | 0.400 |
| First upward turn after nonpositive increment | 0.521 | 0.521 | 0.505 |
| Two consecutive upward lower-band increments | 0.501 | 0.515 | 0.460 |
| Shrinkage of price-minus-middle gap | 0.596 | 0.578 | 0.596 |
| Middle-band increment | 0.620 | 0.588 | 0.661 |
| Close decline | 0.576 | 0.560 | 0.571 |
| Middle rises faster than nondecreasing price, binary | 0.484 | 0.470 | 0.498 |

The earlier period has 82 convergence and 93 continuation episodes; the
later period has 28 and 53. Among all 125 double-lower-rise episodes, 16
converge, 21 continue upward and 88 belong to Other. A double rise alone
does not distinguish the hypothesized outcomes here.

The strongest exploratory association is middle-band movement, not the
lower-band turn. However, the outcome definition itself requires a rising
middle band, so persistence of that indicator may explain part of the
association. No evidence here establishes profitable entries or a usable
exhaustion veto. These are retrospective subwindows of symbols retained in
later scans, not prospectively sampled confirmed BellUp episodes. Rounded
indicators, survivor selection, fixed arbitrary label tolerances and the
lack of independent manual shape labels remain limitations.

## SECZ partial candle: available information versus exact scan state

There are 21 consecutive cached M5 bars from September 9 08:00 through the
09:40 start, providing complete five-minute coverage through 09:45. The
09:45 M5 bar is excluded because it closes after the 09:49:58 scan. The
last safely completed close is 8.17.

Reconstruct conventional Bollinger(20, SMA, 2 population standard deviations)
from the preceding 19 completed H4 closes plus this partial close:

| Point | Upper | Middle | Lower |
| --- | --- | --- | --- |
| Last completed H4, 04:00 start | 8.5120 | 7.1100 | 5.7080 |
| Partial H4, 08:00 start, known through 09:45 | 8.6318 | 7.1995 | 5.7672 |

This supports a second lower-band rise at 09:45. The price-minus-middle
gap falls from 1.1400 to 0.9705, with 0.0895 attributable to middle-band
rise and 0.0800 to price decline. Width still increases: this is not yet
actual band contraction.

But the candidate's TradePlanLiveReferencePrice is 8.61, not 8.17. As a
sensitivity calculation only, using 8.61 as the partial close produces
middle 7.2215, lower 5.7184 and price-minus-middle gap 1.3885. The lower
band still rises, but the gap now widens. The quote has not been verified
as the H4 close at precisely 09:49:58; neither substitution is an exact
replay of that moment. The final partial M5 interval cannot be recovered
from completed M5 candles without lookahead. Cached bars can also have
vendor revisions. These are reconstructions, not Yahoo indicator values.

## Decision

Useful finding now: the user's missing second lower-band rise can be
reconstructed shortly before the scan, but it is not a discriminative
standalone precursor on this history. Middle-band catch-up is a more
promising descriptive feature, with only modest exploratory separation.
The triangle transition and profitable exhaustion are different targets.

Do not enable a veto or change ranking from these results. Exact intrabar
validation requires timestamped partial-H4/indicator snapshots at the
actual decision time. Shape validation also needs manually checked
convergence and continuation cases plus a new, fixed-rule holdout. More
completed H4 candles alone cannot recover the missing intrabar state.
