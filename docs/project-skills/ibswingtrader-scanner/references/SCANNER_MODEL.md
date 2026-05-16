# Scanner Model

## Main purpose

The scanner should surface tickers with strong expected amplitude before the main move finishes.

The scanner should not be judged primarily by entry precision. That is `TradePlan`.

## Two candidate families

### ReversalCandidates

Use reversal logic when the ticker is below the relevant daily mean context.

Typical traits:

- below-mid or recent return toward mean
- pullback / collapse / early turn
- deeper or delayed entry may be acceptable

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

## Evaluation principle

For scanner quality:

- `AmplitudePct >= 10%` is strong
- low amplitude is bad scanner quality
- `NoEntry` alone is not enough to blame the scanner

## Common failure modes

### Fully missed

The ticker is absent from logs and `WishList`.

This is recall/universe/filter failure.

### WishList only

The ticker is seen but does not get promoted.

This is promotion or gating failure.

### In candidates but not summary

The ticker exists in `candidates.json` but not in top summary.

This is ranking failure.

### In top but weak amplitude

This is stale/weak ranking and should be penalized.

## Current project direction

The current direction is:

1. use series as the primary signal
2. push real future-amplitude names into `TodayResearchLikeCandidates`
3. keep `ReversalCandidates` separate
4. only then tune `TradePlan`
