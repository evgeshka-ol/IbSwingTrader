# Series Playbook

These are the main series used to understand a ticker before it fully expands.

## Primary series

- `RecentWeeklyBbMidDistanceSeries`
- `RecentDailyBbMidDistanceSeries`
- `RecentH4BbMidDistanceSeries`
- `RecentWeeklyBbWidthSeries`
- `RecentDailyBbWidthSeries`
- `RecentH4BbWidthSeries`
- `RecentWeeklyBbUpperBandSeries`
- `RecentWeeklyBbMidBandSeries`
- `RecentWeeklyBbLowerBandSeries`
- `RecentDailyBbUpperBandSeries`
- `RecentDailyBbMidBandSeries`
- `RecentDailyBbLowerBandSeries`
- `RecentH4BbUpperBandSeries`
- `RecentH4BbMidBandSeries`
- `RecentH4BbLowerBandSeries`
- `RecentWeeklyMacdSeries`
- `RecentDailyMacdSeries`
- `RecentH4MacdSeries`

## Main interpretations

### Runaway Up

Typical signs:

- `Weekly/Daily/H4` mid distance all positive
- `MACD` positive or recovering
- width not collapsing
- slopes non-destructive

This is usually `TodayResearchLike`.

### Smooth Continuation

Typical signs:

- above-mid but not vertical
- `Daily/H4` MACD positive
- slopes mildly positive or only slightly negative
- width stable or still constructive

This often deserves higher ranking than noisy late movers.

### Cooling But Alive

Typical signs:

- above-mid
- some short-term slopes soften
- `MACD` still alive
- width not fully collapsing

Do not auto-kill this. Often still a valid `TodayResearchLike`.

### Stale Continuation

Typical signs:

- very far above mid
- `Daily/H4` slopes down together
- `MACD` weakens
- width starts losing support

These should fall in ranking.

### Min-first

Bullish higher timeframe, but local correction still pressing.

Typical signs:

- weekly constructive
- daily corrective
- H4 still pushing down or pulling back

This is a valid setup, but execution may require deeper entry.

For `TradePlan`, min-first is not just a label. It means entry should be
predicted from the expected local pullback path. A strong-amplitude loss often
means the entry was too early and the stop was hit before the real move.

### Triangle Growth

Typical signs on H4:

- one large green impulse candle
- then several short candles near the top
- mean rising under price

Entry should be based near the lows of the short consolidation candles, not blindly at the mid.

### Bollinger Squeeze Launch

This pattern should be detected from the real Bollinger curves, not only from
distance-to-band fields.

Use:

- `*BbUpperBandSeries`
- `*BbMidBandSeries`
- `*BbLowerBandSeries`
- `*BbWidthSeries`
- `*BbUpperDistanceSeries` as confirmation that price touches or breaks the
  upper band

Typical bullish shape:

- the band width was squeezed or flat
- the upper band bends upward and accelerates
- the mid band keeps rising, but with a milder bend than the upper band
- the lower band does not follow the upper band upward; it lags, flattens, or
  moves lower, so the envelope opens
- RSI/MACD confirm the impulse

The pattern works on every available timeframe, but the interpretation changes:

- `Weekly`: large background potential; rare but powerful when it aligns.
- `Daily`: main swing / next-day research-like potential, as in TE.
- `H4`: early entry or fast intraday/next-day continuation, as in ONDS.

Do not require the pattern to exist on Daily before using it. A clean H4
Bollinger launch without Daily launch can still be playable, but usually calls
for a faster, more defensive trade plan than a Daily/Weekly launch.

When comparing band curves across tickers, compare shape rather than absolute
price. Normalize by the starting point or compare deltas from the first point.

## Practical use

When comparing scanner output to research winners:

- first compare `Weekly`
- then `Daily`
- then `H4`
- compare the actual row values point-by-point after normalizing each series from its first point
- use modest per-point tolerance, not exact equality; tiny deviations are the
  same shape, but differences beyond tolerance should reduce the match
- prefer close literal similarity to winner rows over broad slope-only matches

The question is:

- does the scanner see the same structural regime early enough
- and if yes, does it rank it high enough
- close winner-template matches can support `TodayResearchLike` promotion for
  live movers when rank, ATR, or entry score confirms the setup

## Template sources

Use two positive template families:

- `research_top_gainers.csv`: primary templates for `TodayResearchLikeCandidates`
- `evaluation-dataset.csv`: high-amplitude templates for both `TodayResearchLikeCandidates` and `ReversalCandidates`

For `ReversalCandidates`, keep templates below-mean and high-amplitude. The goal is
not just being a pullback, but being a pullback shape that historically produced
`AmplitudePct > 10%`.

When tuning `TradePlan`, use the same series to predict:

- pullback depth before continuation
- whether to wait for H4/Daily turn confirmation
- entry timing relative to `MinFirst` / `MaxFirst`
- exit placement for the setup family

Flat discounts from current price are not enough for this project direction.

For entry prediction, keep four distinct profile classes instead of one global
discount:

- fast continuation / winner-template: cap entry near current price
- cooling but alive: moderate pullback
- real below-mean reversal: deep pullback
- overheated late spike: avoid or require a very deep non-chasing entry

A ticker can start below daily mean and still be a fast continuation if H4 is
already accelerating hard. Do not force those rows into deep-pullback entry just
because the daily MA distance is still negative.

For explosive min-first continuation, H4 BB pullback logic must not push entry
too far below current price. Cap the entry discount separately, then tune the
profit target with the setup's default profit percent.

For summary ranking, compare rows in both directions:

- positive templates: `research_top_gainers.csv` and high-amplitude evaluation
  rows should lift candidates even when RSI already looks high
- negative templates: `SameDayContinuation` evaluation rows with
  `AmplitudePct < 10%` should penalize candidates that look unlikely to clear
  the 10% amplitude line

Do not use the negative templates as a hard scanner filter. They are a ranking
tool for keeping weak lookalikes out of the current top-ranked `TodayResearchLikeCandidates` rows.
