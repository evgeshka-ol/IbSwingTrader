# Series Playbook

These are the main series used to understand a ticker before it fully expands.

## Primary series

Prefer real chart-like indicator lines for pattern detection and confidence.
New prediction, filter, promotion, ranking, and trade-plan logic should be
built on these rows first:

- `RecentWeeklyBbUpperBandSeries`
- `RecentWeeklyBbMidBandSeries`
- `RecentWeeklyBbLowerBandSeries`
- `RecentDailyBbUpperBandSeries`
- `RecentDailyBbMidBandSeries`
- `RecentDailyBbLowerBandSeries`
- `RecentH4BbUpperBandSeries`
- `RecentH4BbMidBandSeries`
- `RecentH4BbLowerBandSeries`
- `RecentWeeklyMacdLineSeries`
- `RecentWeeklyMacdSignalSeries`
- `RecentWeeklyMacdHistogramSeries`
- `RecentDailyMacdLineSeries`
- `RecentDailyMacdSignalSeries`
- `RecentDailyMacdHistogramSeries`
- `RecentH4MacdLineSeries`
- `RecentH4MacdSignalSeries`
- `RecentH4MacdHistogramSeries`
- `RecentWeeklyRsiSeries`
- `RecentDailyRsiSeries`
- `RecentH4RsiSeries`

Migration direction:

- Bollinger upper/mid/lower rows are the primary pattern signal.
- MACD line/signal/histogram rows are the second-level confirmation signal.
- RSI rows are a final confidence/ambiguity correction.
- The old `Recent*MacdSeries` compatibility rows should be treated as
  histogram-only aliases, not as complete MACD.
- MA is not a priority visual signal for this project direction. Keep existing
  MA/distance rows only as legacy/context unless a specific analysis proves
  they add value beyond Bollinger/MACD/RSI.
- Distance/width rows can remain for compatibility and diagnostics, but should
  be removed from primary decision logic over time.

Active cleanup supersedes that compatibility allowance for scanner similarity
and `candidates.csv`: only real Bollinger upper/mid/lower, MACD
line/signal/histogram, and RSI rows are admitted. There is no weighted
cross-timeframe total or mean row distance. Daily and H4 are matched
independently; the worst point of the worst real row controls each timeframe.
Weekly rows remain context only.

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

### ReversalHook

`ReversalHook` is the current working daily return-to-mid pattern for the final
`Reversal` path. It is evaluated only after the ticker has already been split
into `Reversal` by the last closed daily close being below the daily Bollinger
mid.

The row shape:

- daily lower Bollinger band was falling and then hooks upward
- daily mid band is weak but decelerates or begins to turn
- upper/lower envelope compresses after the breakdown
- daily MACD histogram turns upward toward zero
- daily MACD line and signal converge
- daily RSI recovers from its recent low

The best timing is the first or second daily bar after the lower-band hook.
POET, ASM, SSRM, CDE, and SVM from the 2026-06-15 evaluation set are the first
accepted working examples.

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

Bell pair:

- `BellUp` is the direct squeeze-to-launch form. The prior phase should be
  compressed or flat, and the recent phase should show clear expansion in the
  upper/mid envelope before the setup is treated as Bell.
- A shorter local turn also qualifies when the latest upper and mid Bollinger
  rows bend upward together and band width starts opening again. This is the
  phase that should catch TE-style green-arrow entries earlier.
- Entry belongs in the squeeze phase near the end of the session, before the
  expansion is obvious.
- `BellDown` is the vertical mirror. The same prior-compression / recent-
  expansion logic applies, but the recent phase breaks down instead of
  launching up.
- In both cases, the decisive cue is the band geometry: the middle band must
  stop compressing in the direction that invalidates the move, and then the
  price should mean-revert toward the mid as the flatting begins.
- One clean timeframe is enough to recognize Bell. The source timeframe sets
  the expected horizon:
  - `H4` means today and may enter final `Runaway`
  - `Daily` means tomorrow and may enter final `Runaway`
  - `Weekly` means wishlist / next week

The final `Runaway` list currently admits only AMLX-like `BellUp` candidates on
real Bollinger rows from `H4` or `Daily`. Weekly-only Bell and non-Bell
continuation shapes are watchlist/diagnostic context, not final `Runaway`
promotion.

When the higher-frame runway shape is present, separate the phase by the saved
pre-move H4 rows:

- `ReadyNow`: UMAC/ONDS-like. H4 upper band is opening upward, H4 mid is not
  falling, H4 lower is not simply following upward, and H4 RSI/MACD do not roll
  over. These rows can be promoted and ranked for same-day trading.
- `NotReady`: SHLS-like. Daily/weekly runway is visible, but H4/Daily trigger
  is not ready. Keep it out of the current final list; it is still runway
  context, not a reversal.
- `Neutral`: neither the higher-frame runway nor the H4 trigger is confirmed
  well enough by the saved rows. Keep it out of the trade-ready
  `Runaway` path even if an older live-mover or template
  branch likes it.

When comparing band curves across tickers, compare shape rather than absolute
price. Normalize by the starting point or compare deltas from the first point.

## Practical use

When comparing scanner output to research winners:

- compare `Daily`
- then compare `H4`
- inspect `Weekly` only as background context
- compare the actual row values point-by-point after normalizing each series from its first point
- treat Daily and H4 as separate matches; either one is enough, without
  averaging it with the other timeframe
- never admit a candidate from a Weekly-only match
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

- `research_top_gainers.csv`: primary templates for `Runaway`
- `evaluation-dataset.csv`: high-amplitude templates for both `Runaway` and `Reversal`

For `Reversal`, keep templates below-mean and high-amplitude. The goal is
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
tool for keeping weak lookalikes out of the current top-ranked `Runaway` rows.
