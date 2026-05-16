# Series Playbook

These are the main series used to understand a ticker before it fully expands.

## Primary series

- `RecentWeeklyBbMidDistanceSeries`
- `RecentDailyBbMidDistanceSeries`
- `RecentH4BbMidDistanceSeries`
- `RecentWeeklyBbWidthSeries`
- `RecentDailyBbWidthSeries`
- `RecentH4BbWidthSeries`
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

### Triangle Growth

Typical signs on H4:

- one large green impulse candle
- then several short candles near the top
- mean rising under price

Entry should be based near the lows of the short consolidation candles, not blindly at the mid.

## Practical use

When comparing scanner output to research winners:

- first compare `Weekly`
- then `Daily`
- then `H4`

The question is:

- does the scanner see the same structural regime early enough
- and if yes, does it rank it high enough
