# Series Playbook

These are the main series used to understand a ticker before it fully expands.

The `Recent*Series` window lengths (Daily/Weekly/H4 lookback, plus the swing-
boundary-detector bounds) are defined once in
`Domain/Settings/RecentSeriesWindow.cs` (as of 2026-08-10). They used to be
duplicated as private consts across `CandidateFinder.cs`,
`EvaluationDatasetBuilder.cs`, `TradeDatasetBuilder.cs`,
`BuildResearchDatasetCommand.cs`, `NormalizeReportsCommand.cs`, and
`HistoricalCache.cs`. Change a window length there, not in any one consumer —
otherwise the scanner, the evaluation dataset, and the research dataset can
silently start looking at different amounts of history for the same ticker.

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

Planned architecture change (2026-09-18): this below-mid prerequisite is a
legacy implementation constraint, not part of the intended pattern
definition. The target classifier will evaluate ReversalHook independently on
every broker-returned ticker and use pattern-first groups (`BellUp`,
`ReversalHook`, `Other`). BBNX is the first transition exemplar: its
ReversalHook crossed above the Daily mid and later continued strongly. Do not
implement this migration from BBNX alone; collect cache-confirmed examples and
validate the independent detector against realized outcomes first.

The row shape:

- daily lower Bollinger band was falling and then hooks upward
- the lower-band hook is strong enough to show a real turn, not only a small
  horizontal support bounce
- the lower-band turn must be fresh: the transition from a negative delta to
  non-negative deltas must still be visible in the last three daily points
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
- Reject a Daily `BellUp` as post-factum when its row phase is already late:
  either the pattern appears only after H4 RSI and MACD histogram have rolled
  over from a local peak, or the Daily pattern already existed on the previous
  point while H4 RSI is elevated and the last two H4 rows show terminal band
  expansion. (This is the Daily-timeframe counterpart to the KMI H4
  terminal-pullback rejection above.)
- After loading the current M15 rows, reject a `Runaway` whose latest closed H4
  close was above the H4 Bollinger mid but whose live M15 price has crossed
  below that same mid. This is a structural invalidation of the saved setup,
  not a fixed percentage-move filter.
- Use M15 only for execution timing after D1/H4 classification. For `Runaway`,
  a confirmed M15 `BellUp` predicts entry near the rising M15 mid or the latest
  shallow pullback low. For `Reversal`, a confirmed M15 `ReversalHook` predicts
  entry near the hooked lower band or the latest local low. If the matching
  M15 pattern is unavailable, retain the existing entry forecast as fallback;
  M15 must not change the D1 family split.

The final `Runaway` list currently admits only AMLX-like `BellUp` candidates on
real Bollinger rows from `H4` or `Daily`. (The template-match ranking support
and low-amplitude veto mentioned in older notes were removed 2026-09-01 — see
`SCANNER_MODEL.md` "Series-template matching"; the veto had never actually
fired in the feature's history.) Weekly-only Bell and non-Bell continuation
shapes are diagnostic context, not final `Runaway` promotion.

When strict promotion returns no candidates in either family, keep the result
empty.

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

## BellUp entry timing (user clarification, 2026-09-09)

The active objective is a playable `Runaway` list containing only BellUp,
at the right moment before the boost. Recognizing the BellUp shape and
deciding whether entry is already late are separate checks. Setups matching
neither playable family belong in `Other`.

Apply the following timing rule on the timeframe of the confirmed BellUp
(Daily or H4), using aligned Open/Close candles and signed body
`B = Close - Open`. Do not require both timeframes to pass this body test.
Let T-1 be the previous completed
candle, T-2 the completed candle before it, and T-3 the one before that:

1. If `B(T-1) > 0` and `B(T-1) >= 2 * abs(B(T-2))`, the boost happened on
   T-1: do not enter.
2. Otherwise, if `B(T-2) > 0` and `B(T-2) >= 2 * abs(B(T-3))`, the boost
   happened on T-2: do not enter.
3. Otherwise, retain the ticker as a playable candidate, provided its BellUp
   and other eligibility checks pass.

The boost candle must be green; its predecessor can be either color, since
the comparison uses the predecessor's absolute body length. This is a
body-size comparison, not a wick/range or close-to-close percentage test.
Three completed candles are needed to check both positions. Determine
completion from candle timestamps; Daily series may already exclude the
forming candle while H4 series may include it. Do not blindly use identical
array offsets for both timeframes. Missing history cannot establish that
both checks passed; the implementation sends such a setup to `Other` with
an insufficient-history reason.

This is the user's requested behavior, not a claim of AUC/holdout validation.
It supersedes the older Daily 5% close-to-close definition in code comments.

### Implementation (2026-09-09)

`Application/Candidates/BellUpEntryTiming.cs` implements the shared body
rule for the selected Daily or H4 timeframe. `ClassifyBellPatternSignal`
selects the pattern's timeframe using the existing classifier policy;
`CandidateFinder.TryAddCandidate` applies the timing check to
otherwise eligible BellUp candidates before building a trade plan. A boost
on that timeframe sends the row to `Other`, with a zero plan and a reason
identifying the timeframe and T-1/T-2 candle. No boost means this timing gate
passes, not that unrelated eligibility checks are bypassed.

The check uses raw candles without rounding the bodies, preferring the
context's D1 candles and falling back to the existing H4-to-Daily aggregation
only when D1 candles are unavailable. Times are market-local as prepared by
the existing data pipeline. Daily completion uses the regular 16:00 session
close; H4 completion uses candle start plus four hours. A green candle after
a zero-body candle satisfies the literal 2x rule; two zero bodies do not.

The trade-plan readiness profile uses the same predicate. The old Daily
5% close-to-close test, H4 single-position check, and late-phase ranking
offset/penalty application were removed; the rule now controls admission.
Existing BellUp geometry and other late-phase checks remain in effect.

### SECZ chart clarification (2026-09-09)

The user's annotated charts `Data/Charts/SECZ-D.png` and
`Data/Charts/SECZ-H4.png` distinguish pattern onset from individual boosts:

- Daily: the user identifies the September 8 upper/lower-band bends and
  beginning expansion as the start of the Daily pattern. That day's later
  Daily shape was not a completed pattern available to justify or veto the
  September 8 morning H4 entry.
- H4: the red arrow marks BellUp onset well before the scan; green arrows
  mark successive boosts within the same ongoing pattern. The yellow arrow
  marks the user's scan candle, followed by the profitable boost.
- A BellUp can contain repeated boosts separated by pauses. An old boost
  does not invalidate the whole pattern forever. The two-candle timing
  window is local to the selected pattern timeframe.

The initial September 9 implementation incorrectly required both Daily and
H4 to pass. That cross-timeframe body veto has been removed. This correction
does not itself prove that the new filter retains historical SECZ: the
cached completed H4 candles before the September 8 07:51:20 scan include
September 4 12:00 `7.38 -> 7.41` and 16:00 `7.42 -> 7.52`. The literal
2x rule calls the latter's small `+0.10` body a boost against `+0.03`.
The user's chart does not mark these small steps as boosts, and the yellow
chart candle is red whereas the cached September 8 04:00 bar is green.
Align chart/broker candle intervals and session coverage before equating
those candles or changing boost thresholds. Do not invent a body-size floor
or claim SECZ is fixed solely from the timeframe correction.

### Canonical H4 and chart-aligned H4 (2026-09-09)

The scanner now keeps two explicitly different intraday views:

- **Canonical H4:** bars returned by IB/TWS (`04:00`, `08:00`, `12:00`,
  `16:00` in the SECZ cache). Bollinger, MACD, RSI and BellUp classification
  continue to use this complete, sequential four-hour series.
- **ChartH4:** a secondary experimental view built from cached M15 bars with
  session anchors `08:00–09:30`, `09:30–13:30`, and `13:30–16:00`. It is
  retained only for comparison and diagnostics. It does not affect admission
  or body timing; the gate uses canonical TWS H4.

`SessionAlignedH4Builder` does not replace the canonical series or recalculate
its indicators. Historical comparison must measure both representations on
the same snapshots before changing BellUp classification or ranking.

Initial offline comparison (September 9, 2026) found 415 historical BellUp
rows with both cached H4 and M15 data. The body verdicts differed materially:
the canonical and chart-aligned views both flagged a recent boost on 107 rows;
canonical-only flagged 55; chart-only flagged 135. This is a representation
effect, not enough evidence to choose a threshold. SECZ's historical Win is
still flagged by both views because its immediately preceding chart-aligned
bars include an earlier green expansion. The example therefore exposes a
second unresolved dimension: **boost freshness**. A repeated old boost inside
an ongoing BellUp does not necessarily mean the next session's entry is late.
Do not add a numeric age window until the chart labels and outcomes are
measured together. A later Yahoo chart refresh showed the ordinary H4 grid
restored (`08:00`, `12:00`, `16:00`), matching TWS. Treat the earlier
`09:30` alignment as a transient chart display issue; do not optimize scanner
logic around it.

Focused checks are in `tests/ScannerTimingChecks`, with no broker or NuGet
dependencies. User-run command:
`dotnet run --project tests/ScannerTimingChecks/ScannerTimingChecks.csproj`.
These checks were added but not executed by Codex, following the project's
execution boundary. Live scanner/evaluation behavior still needs a user run.

## Practical use and template sources — removed 2026-09-01

This section used to describe how to compare scanner output against
literal-template winner rows (positive/negative template families sourced
from `candidates.csv`, point-by-point comparison after normalization, etc.).
That machinery was deleted from the codebase 2026-09-01 after its diagnostic
output was found to have recorded zero real matches across its entire
history — see `SCANNER_MODEL.md` "Series-template matching" for the full
history and `SETTINGS_MAP.md` for the removed settings block. Use
`RankingQualityScore` (`CalculateRunawayLaunchQualityScore` /
`CalculateReversalHookQualityScore` in `CandidateFinder.cs`) to compare
candidates instead — validate any new feature's AUC against
`evaluation-dataset.csv` before adding it, same discipline as before.

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

Summary ranking no longer compares against positive/negative template rows
(that mechanism is removed — see "Practical use and template sources" above);
it runs on `RankingQualityScore` alone.
