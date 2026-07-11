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
- the lower-band hook is strong enough to show a real turn, not only a small
  horizontal support bounce
- daily mid band is weak but decelerates or begins to turn
- upper/lower envelope compresses after the breakdown
- daily MACD histogram turns upward toward zero
- daily MACD line and signal converge
- daily RSI recovers from its recent low
- price stops making lower closes and begins compressing the distance back to
  the daily mid

Reject descending-triangle or drift-under-mid cases as `ReversalHook`. In
those cases price may oscillate along a support/diagonal while the daily mid is
still almost linear; the likely path is the mid moving toward price while the
figure closes, not price exploding toward the mid. TSN on 2026-06-24 is the
working negative example.

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
  - `Weekly` is context only and never decides final matching
- A valid H4 `BellUp` may be a gradual squeeze launch, not only a final local
  kink. The upper band should pull away from a rising mid while the lower band
  lags, flattens, or opens down; do not require the final point to be the
  strongest acceleration point.
- Reject near-parallel upward translations of all three bands as `BellUp`.
  If the lower band rises materially with the mid, the envelope is not opening
  cleanly enough for this pattern.
- Reject an H4 `BellUp` that has already rolled into a terminal pullback. A
  setup can be valid on the prior H4 segment and still be unsafe now when the
  latest red candle moves by body from the upper-band zone back toward the mid,
  RSI rolls over, and MACD/band geometry begins to close or bend against the
  move. KMI on 2026-06-24 is the working negative example.
- A Daily `BellUp` or `ReversalHook` still needs H4 not to contradict the setup
  for today's trade-ready list. Weekly can strengthen context, but Weekly must
  not be the reason a candidate is admitted.

The final `Runaway` list currently admits only AMLX-like `BellUp` candidates on
real Bollinger rows from `H4` or `Daily`. A high-amplitude Runaway template
match on either timeframe supports ranking but is not mandatory. A
low-amplitude template that is at least as close as the positive match vetoes
strict promotion. Weekly-only Bell and non-Bell continuation shapes are
diagnostic context, not final `Runaway` promotion.

When strict promotion returns no candidates in either family, keep the result
empty. Never disable the low-amplitude veto simply to fill the list.

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

Use two positive template families, both sourced from historical
`candidates.csv` snapshots:

- scan snapshots later confirmed as high-amplitude `Runaway` rows
- scan snapshots later confirmed as high-amplitude `Reversal` rows

`evaluation-dataset.csv` supplies the amplitude outcome and exact scan key
only. `research_top_gainers.csv` is an outcome oracle, not a template feature
source.

Template admission is amplitude-based, not pattern-verdict based. A row that
later produced high amplitude should become a positive ranking template even
when the current `PatternVerdict` says `Mismatch/None`. The pattern verdict is
diagnostic context; the ranking target is whether future high-amplitude names
rise above weak names.

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

- positive templates: original scan snapshots with a later confirmed pattern
  and high amplitude should lift candidates even when RSI already looks high
- negative templates: original `Runaway` scan snapshots whose later evaluation
  had `AmplitudePct < 10%` should push down candidates that look unlikely to
  clear the 10% amplitude line

Do not use the negative templates as a hard scanner filter — a vetoed
candidate stays in the list, it does not get removed from admission. Since
2026-07-11 the veto is a hard rank-tier demotion rather than a subtracted
score: a candidate whose positive template match would otherwise be
`Confirmed`/`Weak` drops straight to the same `None` rank tier as a candidate
with no template match at all, whenever a low-amplitude template matches it at
least as closely. It is a ranking tool for keeping weak lookalikes out of the
current top-ranked `Runaway` rows, not an admission filter.
