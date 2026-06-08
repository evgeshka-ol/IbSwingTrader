# Scanner Model

## Main purpose

The scanner should surface tickers with strong expected amplitude before the main move finishes.

The scanner should not be judged primarily by entry precision. That is `TradePlan`.

## Current top priority

The nearest minimal target is top-1 quality:

- the #1 current `TodayResearchLikeCandidates` row should be stable enough to
  play
- it should convert into a practical winner
- the plan should capture more than 10%

Use this as the first decision point before optimizing broad recall or average
list quality.

The highest-priority scanner goal is:

- tickers in today's current top-ranked `TodayResearchLikeCandidates`
- should appear in tomorrow's `research_top_gainers.csv`

This is the main next-day feedback loop. If this relationship is weak, tune
scanner recall, promotion, and ranking before tuning entries/exits.

## Two candidate families

### ReversalCandidates

Use reversal logic only when the ticker is below the daily Bollinger mid.

Hard rule:

- If the ticker is below the daily Bollinger mid, it is a `ReversalCandidates`
  candidate.
- If the ticker is at or above the daily Bollinger mid, it is not a
  `ReversalCandidates` candidate.
- Weekly and H4 context may refine the reversal subtype or trade plan, but they
  do not change the family assignment.

Typical traits:

- below-mid or recent return toward mean
- pullback / collapse / early turn
- deeper or delayed entry may be acceptable

Expected quality:

- should still produce `AmplitudePct > 10%` often enough to matter
- lower amplitude is scanner failure, even if the trade plan avoided entry

### TodayResearchLikeCandidates

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
2. make `TodayResearchLikeCandidates` predict tomorrow's research dataset
3. keep `ReversalCandidates` separate
4. require `ReversalCandidates` to produce meaningful amplitude too
5. only then tune `TradePlan`

## Series-template matching

The scanner should move toward literal row-shape matching.

For `TodayResearchLikeCandidates`:

- compare the current candidate series against rows in `research_top_gainers.csv`
- also compare against high-amplitude rows in `evaluation-dataset.csv`
- treat close matches to research winners as promotion/ranking evidence
- compare against today's low-amplitude evaluation rows as negative templates;
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

For `ReversalCandidates`:

- compare only against high-amplitude reversal rows from `evaluation-dataset.csv`
- do not let above-mean continuation templates promote reversal candidates

Comparison principle:

- compare each series point-by-point with modest tolerance
- normalize each compared series from its first point
- use Daily, Weekly, and H4 contexts together
- avoid replacing this with only slope/aggregate statistics
- split templates by outcome role: high-amplitude rows are positive scanner
  templates; low-amplitude rows are rejection templates
