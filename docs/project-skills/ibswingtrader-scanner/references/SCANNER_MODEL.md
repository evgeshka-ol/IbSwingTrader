# Scanner Model

## Main purpose

The scanner should surface tickers with strong expected amplitude before the main move finishes.

The scanner should not be judged primarily by entry precision. That is `TradePlan`.

## Current top priority

The highest-priority scanner goal is:

- tickers in today's summary `TodayResearchLikeCandidates`
- should appear in tomorrow's `research_top_gainers.csv`

This is the main next-day feedback loop. If this relationship is weak, tune
scanner recall, promotion, and ranking before tuning entries/exits.

## Two candidate families

### ReversalCandidates

Use reversal logic only when the ticker is below the relevant daily and weekly
mean context.

Hard rule:

- If the ticker is above mean on any relevant mean context, it is not a
  `ReversalCandidates` candidate.
- A `ReversalCandidates` candidate must be below mean on both daily and weekly
  context.
- Above-mean tickers may only be considered through `TodayResearchLikeCandidates`
  continuation/promotion logic.

Typical traits:

- below-mid or recent return toward mean
- pullback / collapse / early turn
- deeper or delayed entry may be acceptable

Expected quality:

- should still produce `AmplitudePct > 10%` often enough to matter
- lower amplitude is scanner failure, even if the trade plan avoided entry

### TodayResearchLikeCandidates

Use continuation logic when the ticker is above the mean and acting like a live winner.

Typical traits:

- above-mid on `Weekly/Daily/H4`
- positive or recovering `MACD`
- supportive Bollinger width / distance structure
- strong live preset such as:
  - `HOT_BY_VOLUME`
  - `MOST_ACTIVE`
  - `TOP_PERC_GAIN`
  - `TOP_OPEN_PERC_GAIN`

Expected quality:

- should be the closest proxy for next-day `research_top_gainers.csv`
- misses here are the first thing to fix

## Evaluation principle

For scanner quality:

- `AmplitudePct >= 10%` is strong
- low amplitude is bad scanner quality
- `NoEntry` alone is not enough to blame the scanner

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

For `ReversalCandidates`:

- compare only against high-amplitude reversal rows from `evaluation-dataset.csv`
- do not let above-mean continuation templates promote reversal candidates

Comparison principle:

- compare each series point-by-point with modest tolerance
- normalize each compared series from its first point
- use Daily, Weekly, and H4 contexts together
- avoid replacing this with only slope/aggregate statistics
