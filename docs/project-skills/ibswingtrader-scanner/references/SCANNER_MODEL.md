# Scanner Model

## Main purpose

The scanner should surface tickers with strong expected amplitude before the main move finishes.

The scanner should not be judged primarily by entry precision. That is `TradePlan`.

## Current top priority

The nearest minimal target is top-1 quality:

- the #1 current `Runaway` row should be stable enough to
  play
- it should convert into a practical winner
- the plan should capture more than 10%

Use this as the first decision point before optimizing broad recall or average
list quality.

The highest-priority scanner goal is:

- tickers in today's current top-ranked `Runaway`
- should appear in tomorrow's `research_top_gainers.csv`

This is the main next-day feedback loop. If this relationship is weak, tune
scanner recall, promotion, and ranking before tuning entries/exits.

## Two candidate families

### Reversal

Use reversal logic only when the ticker is below the daily Bollinger mid.

Hard rule:

- Use the last closed daily bar for this check, not the current intraday
  partial bar.
- Resolve that closed-bar date from the dominant latest D1 date across the
  current scan universe. A market holiday must not be treated as a missing bar.
- If the ticker was below the daily Bollinger mid on the last closed daily bar,
  it is a `Reversal` candidate.
- If the ticker was at or above the daily Bollinger mid on the last closed
  daily bar, it is not a
  `Reversal` candidate.
- Weekly and H4 context may refine the reversal subtype or trade plan, but they
  do not change the family assignment.

Typical traits:

- below-mid or recent return toward mean
- pullback / collapse / early turn
- deeper or delayed entry may be acceptable

Expected quality:

- should still produce `AmplitudePct > 10%` often enough to matter
- lower amplitude is scanner failure, even if the trade plan avoided entry

Final `Reversal` promotion currently requires `ReversalHook` after the hard
below-mid split. The pattern is daily-row based:

- lower Bollinger band breaks down, then hooks upward
- daily mid weakens but decelerates or starts turning
- the channel compresses after the breakdown
- MACD histogram turns upward toward zero
- MACD line and signal converge
- RSI recovers from the recent low

After the hook passes, literal series similarity to a high-amplitude reversal
template on real closed D1 or saved H4 rows is supporting evidence, not a hard
admission requirement. Weekly rows remain context only.


Use POET, ASM, SSRM, CDE, and SVM from the 2026-06-15 evaluation set as the
initial working examples. The cleanest entry is normally one to two daily bars
after the lower-band hook.

### Runaway

Use continuation logic when the ticker is at or above the daily Bollinger mid
and acting like a live winner.

Typical traits:

- above-mid on `Weekly/Daily/H4`
- positive or recovering `MACD`
- supportive Bollinger width / distance structure
- strong live preset such as:
  - `HOT_BY_VOLUME`
  - `MOST_ACTIVE`
  - `TOP_PERC_GAIN`
  - `TOP_OPEN_PERC_GAIN`

Internal subtypes inside this family:

- `BellUp`
- `Runaway`
- `LaunchContinuation`
- `PullbackContinuation`

Expected quality:

- should be the closest proxy for next-day `research_top_gainers.csv`
- misses here are the first thing to fix

Important:

- Treat the family boundary as fixed once the daily mid split is correct.
- Further scanner work should change only whether a candidate reaches the final
  list, how it is ranked, and how the trade plan is shaped.
- Do not reopen the family split when the problem is really profit capture or
  top-of-list quality.

## Evaluation principle

For scanner quality:

- `AmplitudePct >= 10%` is strong
- low amplitude is bad scanner quality
- `NoEntry` alone is not enough to blame the scanner
- if roughly two thirds of candidates clear `AmplitudePct >= 10%`, preserve
  that high-amplitude pool and focus scanner work on rejecting the remaining
  low-amplitude third by row similarity
- if live scanning still needs a late guard, use a row-based envelope-expansion
  proxy as the last gate before the final list; do not use the realized
  `AmplitudePct` itself because it is unknown at scan time

For trade-plan quality:

- count of `Win` rows shows how many scanner opportunities the plan converted
- many `Loss`, `NoEntry`, or still-`Open` rows with strong amplitude indicate trade-plan failure
- do not use the number of wins alone to judge scanner quality

## Common failure modes

### Fully missed

The ticker is absent from logs and `WishList`.

This is recall/universe/filter failure.

### WishList only

The ticker is seen but does not get promoted.

This is promotion or gating failure.

### In candidates but not summary

The ticker exists in `candidates.csv` but not in current top-ranked rows.

This is ranking failure.

### In top but weak amplitude

This is stale/weak ranking and should be penalized.

## Current project direction

The current direction is:

1. use series as the primary signal
2. make `Runaway` predict tomorrow's research dataset
3. keep `Reversal` separate
4. require `Reversal` to produce meaningful amplitude too
5. only then tune `TradePlan`

## Series-template matching

The scanner should move toward literal row-shape matching.

For `Runaway`:

- use evaluation rows only to identify historical scan keys that produced
  confirmed `BellUp` and high amplitude
- load the actual positive template series from the matching historical
  `candidates.csv` scan snapshot
- compare against scan snapshots whose later evaluation had low amplitude as
  negative templates;
  candidates whose rows look like the low-amplitude third should be demoted or
  filtered before they occupy the top of the list
- treat the TE-style fresh expansion as a high-priority winner pattern:
  - weekly MA row recovers from negative/below-mean history into positive territory
  - daily MA, daily Bollinger width, and daily RSI expand together
  - H4 MA, H4 Bollinger width, and H4 RSI also expand and hold
  - weekly/daily/H4 MACD are recovering or positive
  - this pattern can outrank a conflicting low-amplitude template match because
    the row structure matches a practical next-day winner
- treat real Bollinger band curve launches as scalable patterns:
  - use `*BbUpperBandSeries`, `*BbMidBandSeries`, and `*BbLowerBandSeries`
    alongside width and upper-distance confirmation
  - the same squeeze/launch shape can matter on Weekly, Daily, or H4
  - Daily launch is stronger next-day research evidence; H4 launch is often an
    earlier intraday/next-day trigger; Weekly launch is broad swing background
  - do not discard a candidate only because the pattern appears on H4 rather
    than Daily; adjust ranking/trade profile by timeframe instead

For `Reversal`:

- use confirmed high-amplitude `ReversalHook` evaluation labels, then load the
  feature rows from the matching historical scanner snapshots
- do not let above-mean continuation templates promote reversal candidates

Comparison principle:

- evaluation provides labels, never template feature values
- compare each series point-by-point with modest tolerance
- normalize each compared series from its first point
- calculate a separate distance for Daily and H4; matching either timeframe is
  sufficient, and the weighted average must not decide admission
- keep Weekly rows as context only; a Weekly-only match must not promote a
  candidate into today's final list
- avoid replacing this with only slope/aggregate statistics
- split templates by outcome role: high-amplitude rows are positive scanner
  templates; low-amplitude rows are rejection templates

Bell pattern pair:

- `BellUp` is the direct squeeze-to-launch form. It requires a prior
  compressed/flat phase and a recent phase with clear expansion; it belongs on
  the above-mid / continuation side.
- `BellDown` is the vertical mirror with the same prior-compression /
  recent-expansion requirement. It belongs on the below-mid / reversal side.
- Use the same real Bollinger upper/mid/lower rows to recognize both forms;
  only the direction changes.
- The scanner matches Bell only on H4 and Daily. One clean matching timeframe
  is enough. The source timeframe controls timing:
  - `H4` means the setup can be played today and may enter final `Runaway`
  - `Daily` means the setup is for tomorrow and may enter final `Runaway`
  - `Weekly` remains background context and is not passed to the Bell matcher

For the current strict `Runaway` pipeline, final promotion requires `BellUp` on
real Bollinger rows in `H4` or `Daily`. Literal Daily/H4 winner similarity
supports ranking, while a closer low-amplitude BellUp match vetoes promotion.
Weekly-only Bell and other continuation subtypes remain context.

If strict promotion leaves both final families empty, an experimental fallback
may retry real Daily/H4 `BellUp` candidates without the low-amplitude template
veto and retain at most the four highest-ranked names. It must not bypass Bell
geometry, readiness, family split, or the other promotion guards. Preserve
these rows for evaluator feedback.
