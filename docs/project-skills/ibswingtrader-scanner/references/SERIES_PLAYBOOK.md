# Series Playbook

Current computational contract (2026-10-17):
[Bollinger EMA and fresh H4 recovery](BOLLINGER_EMA_AND_RECOVERY.md).
EMA20 replaces the former SMA Bollinger center across live feature consumers;
older dated examples below retain their original SMA values. Plain MA context
fields are unchanged. Historical curve matching/validation is not automatic
validation of the EMA model.

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

Literal scanner series-template matching was removed on 2026-09-01;
there is no active cross-timeframe point-distance matching rule. See
[model history](MODEL_HISTORY.md) for the former worst-point comparison.
Current candidate fields and compatibility are documented in SETTINGS_MAP.md.
Weekly remains context; confirmed patterns are evaluated on Daily/H4.

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

Since 2026-10-06, ReversalHook recognizes recent episodes independently on
completed D1 and H4 prefixes. H4 is checked even with sufficient D1 history.
BellUp retains precedence; a recognized but non-ready BellUp is not relabelled
as a reversal. Triangle remains a fallback diagnostic.

Recognition succeeds if either:

- a recent middle-band bend follows three negative deltas and a delta that
  improves on the previous one and is at least 65% of their average, while
  price rises from below mid and MACD histogram improves; or
- the historical strict hook rule matches a completed prefix within the last
  six bars. The below-mid requirement applies at that historical seed, not to
  the latest price. Lower-band/RSI/compression requirements remain part of this
  legacy seed, rather than universal requirements for every episode.

This is a user-authorized recognition change, not empirically validated new
ranking weights. Flat prior mid bands cannot establish the middle-band seed.
No future outcomes or incomplete candles establish either seed.

Phases: `Preparing` (recovery without a recognized seed), `Active` (ongoing
recovery below mid), `MidCrossed` (ongoing recovery after crossing mid),
`Completed` (close or current price reached upper band), and `Stalled`
(recovery no longer holds). A pause does not erase an established episode:
price must remain above the pre-bend close, histogram at least at the bend's
level, and latest close above the three-bar minimum. Fresh price below the
pre-bend close invalidates recovery. Historical seeds use a local
price/histogram continuation check when no discrete mid bend is available.
The old `TargetReached` and MACD-led Preparing labels remain in historical data.

Both timeframe diagnoses, age and completed-bar timestamps are retained in
`PatternVerdictReason`. A recognized active episode is preferred; if none is
active, recognition is still recorded with its phase. H4 crossing does not
imply Daily completion. Middle crossing is no longer an automatic rejection.

Since 2026-10-07, before ReversalHook diagnosis, non-BellUp candidates load an
M5 bridge from the last completed native H4 bar's end (`bar.Time + 4h`) to now.
GetCandlesRange reuses cached M5 and requests missing ranges with its existing
overlap policy. Use exchange-time timestamps, not chart display times.
Only completed M5 are used here, with the current/immediately preceding
bucket rule to prevent old-session prices being treated as current. Without
recent M5, the diagnosis explicitly uses HistoricalSnapshot. The bridge runs
even without a recognized seed, before the recognition rejection.

An established bend can resume at a live mid reclaim when current price is
above mid and the pre-bend close, and either the closed histogram is no worse
than at the bend or a fresh close-only provisional H4 histogram improves
against its previous closed H4 value. See BOLLINGER_EMA_AND_RECOVERY.md for
native-interval, timestamp and history guards. A future MACD crossover is not presumed certain. Fresh price
alone does not establish a historical seed, and incomplete H4/Daily bars do
not become confirmed. M5 diagnostic price/provenance are saved in the candidate.
Final fresh-M5 publication validation remains separate. The bridge adds
possible missing-data requests for the non-BellUp pool, not an unconditional
download of all M5 history. BellUp timing is unchanged.

Since2026-10-28, admitted ReversalHook EntryPrice equals the fresh published
ScanPrice exactly; legacy DeepPullback/series discounts and M15 entry forecasts
do not apply. Target and stop are rebuilt from that entry. See
[direct reversal entry](REVERSAL_SCAN_PRICE_ENTRY.md) for the publication contract.

Before publication, only the deduplicated recognized shortlist refreshes H4,
Daily and fresh M5. Recognition and phase are recalculated. Admission requires
an Active/MidCrossed episode, existing higher-timeframe support and a valid
fresh-price plan. Since2026-10-29, a recognized D1 DeepDeclineRecovery with
gentle W1 and improving native D1 MACD may instead receive the bounded
[AnticipatoryD1Recovery admission](WEEKLY_MID_SLOPE.md), retaining LocalPhase=Stalled.
It requires fresh valid H4 evidence, at least2% room to both D1/W1 mids and
price at/above the last completed D1 low; H4 momentum improvement is not
required. Ordinary active gentle-W1 support still requires closed/fresh H4
improvement. W1 Collapse, Completed, unsupported Stalled, missing fresh quote,
lost recognition or failed support produces Other with zero trade levels and explicit `NotReady`.
If refreshed history newly recognizes BellUp, the pending reversal is retained
as Other with the precedence reason, rather than publishing a reversal plan
for that BellUp. The next scan can apply the normal BellUp pipeline.
Since 2026-10-10, if fresh M5 is absent or its request fails, the recognized
publication shortlist may use a short-lived IB live quote request. It accepts
a positive uncrossed bid/ask within the configured spread limit and uses ask
for a buy reference. Without a valid book, an RTVolume trade with a checked
exchange timestamp may supply the reference. A known wide/crossed current
book cannot be bypassed by a last trade. Only marketDataType=1 is accepted;
unknown, delayed and frozen modes fail. Quote observations do not create OHLC
bars or establish a pattern seed. Fresh-M5 remains preferred.

`PriceSource`, trade reference source/time/observed time and publication status
distinguish `IBLiveAskObserved`, `IBLiveLastTrade` and M5. Ask time denotes
receipt time because top-of-book does not supply an exchange timestamp;
trade time comes from the atomic RTVolume event. Both use the configured age
limit; fallback prices are checked again after plan construction. This quote
fallback applies only to already recognized ReversalHook publication, not
BellUp, diagnostic universe probing, or historical bar reconstruction.

No historical-price fallback authorizes a ReversalHook entry. Since
2026-10-21 an active H4 recovery may bypass D1 Down/lagging Daily MACD through
the bounded [H4-led support rule](REVERSAL_H4_DAILY_SUPPORT.md). D1 Collapse
still vetoes; D1 hooks retain their Weekly checks. Recognition alone does
not guarantee admission.

### Supplemental closed/partial H4 diagnostics (2026-10-10)

Since 2026-10-16, the same supplemental switch also records
`ReversalLowerBandBody(DiagnosticOnly=True, ...)` independently for closed
H4 and D1 in each phase reason/log. This records the user's distinction
between abrupt drop/pause/rebound and gradual drift along the lower band;
it does not introduce another recognition seed or a mandatory lower-above-
body condition for ordinary ReversalHook.

The last four completed candles retain exact OHLC/volume and lower band,
LowerVsBody (AboveBody when Lower > max(Open,Close), InsideBody including
boundary touches, BelowBody, or Unavailable), signed body percentage and
absolute body/range ratios to the median of up to five strictly preceding
candles. ReferenceBars makes short baselines explicit; zero/missing medians
produce n/a. Last/previous positions and PositionChangedIntoBodyFromAbove
describe relative states, not proof the line itself caused a crossing.
Decision time and each bar start are retained; forming H4/D1 are excluded
using existing start+4h / 16:00 conventions. No future rebound is an input.

No numeric sharp-drop or short-pause thresholds have been validated here;
the four-point reporting window and five-point baseline are descriptive
choices, not calibrated admission rules. This context has no effect on
recognition, phase, support, freshness, ranking, plans or candidate group.
Existing phase parsing is unchanged. It reuses prepared closed bars/features,
adds no broker calls and isolates diagnostic failures. Two TSLA screenshots
(`TSLA-Л-pattern-ReversalHook.png`, `TSLA-ReversalHook.png`) motivate the
distinction; their timeframe and exact episode dates are not established
by the cropped images. Do not claim exact historical TSLA replay.
User-run checks: `dotnet run --project tests/ReversalLowerBandBodyChecks`.

When `Finder.ReversalSupplementalDiagnostics` is enabled, the Reversal episode
reason contains `ReversalSupplemental(DiagnosticOnly=True, ...)` independently
of confirmed recognition. `ClosedH4Gradual` checks four negative mid deltas,
at least two improvements among their three adjacent comparisons, latest delta
above the first, rising price versus two bars earlier, improving histogram
with one allowed pause, and prior price below mid. This exploratory diagnostic
supplements the canonical discrete bend; it is not another admission path.

The current native H4 is reconstructed only from completed M5 returned by the
current run's existing bridge/publication requests, never from the cached final
H4 high/close. `PartialStart`, decision time, M5 counts and last observed M5 are
saved. Fresh calculation requires coverage from interval start, no internal
five-minute gaps, at least one positive-volume bar and a positive-volume close
no older than ten minutes after its bar end. The last observed trading close
is used; zero-volume placeholders cannot establish a signal by themselves.
Otherwise `PartialStatus` explains missing/stale/incomplete coverage.

For fresh coverage, exact OHLC, volume, Bollinger bands, MACD line/signal/histogram,
RSI, `PartialGradual` and provisional phase/bend age are saved. Calculations
use a separate copied H4 prefix; confirmed series remain closed-bar series.
Partial bars from fresh snapshots are excluded until their requested-time
boundary has passed their close. Diagnostic errors do not reject a candidate.

`PartialPhase` and gradual recovery are research fields only. They do not alter
BellUp precedence, confirmed episodes, higher-timeframe support or fresh-price
publication rules. See `docs/investigations/2026-10/research-2026-10-10-reversal-recognition.md` for
the evidence and its retrospective cache-availability limitations.

The reversal publication plan uses the fresh M5 price under reversal entry
policy, without BellUp body projection or BellUp target reduction. Its status
is `ReversalFreshM5`; rejected plans use `ReversalRejected`. Evaluation preserves
saved recognition/phase/readiness reasons instead of inventing a later signal.

`ReversalHook` is the current return-to-mid pattern for the pattern-first
`ReversalHook` output group. Since the 2026-09-23 migration, it is evaluated
independently of the former below-Daily-mid family split. The Daily mid can
still provide context, but it is not a hard ReversalHook admission gate.
BBNX is a transition exemplar; do not treat this single example as validation
of general predictive performance.

The legacy strict seed row shape (one recognition route):

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

### Л-Pattern: rising slope followed by a fall toward mid

The user's 2026-10-15 observation: a smooth sloping rise, sometimes initially
BellUp-like, followed almost immediately by a large red candle toward the
Bollinger mid resembles the Cyrillic letter Л. PENG Daily on October 14 is
the working example; the user identifies the October 13 upper-band bend as
the useful preceding warning. The later chart comparison established that
the sharp bend is clearest on the falling candle. The earlier slowdown is
now diagnostic only, not a complete Л classifier or new output group.
See `docs/investigations/2026-10/investigation-2026-10-15-peng-l-pattern.md` for saved evidence.

### П-Pattern and upper-band pause diagnostic (2026-10-15)

The user's NVAX H4 example has a sharp rise, a short sideways bridge and a
similarly sharp fall: Cyrillic П. Its shared warning with PENG's Л is upper-
band slowing around a pause. The initially authorized filter was withdrawn
after discussion: the preceding candles do not establish the later fall.
The current instruction is to collect diagnostics and seek additional
features that distinguish continuation pauses from declines.

On the selected confirmed BellUp timeframe only, use the latest four completed
native candles and exact unrounded upper/mid values. Let a/b/c be the last
three upper-band increments. Record a pause when a > b > 0 and c < b,
the maximum of the middle two closes exceeds the first close, the last close
does not exceed the previous one, remains strictly above mid, and mid is
flat/rising on the last transition. Inputs must be aligned, positive and
chronological with upper > mid. This is two-step deceleration plus stalled
price, not a calibrated numerical definition of a visually sharp bend.

`BellUpUpperBandSlowdown.IsPause` implements the geometry. After admission,
ranking and publication, matching output rows receive
`BellUpPauseDiagnostic(DiagnosticOnly=True, ...)` in PatternVerdictReason and
Context.Notes, plus a `BellUp pause diagnostic` log. This covers selected
BellUp patterns in playable rows and Other rejected by separate gates.
Saved details include timeframe, original decision time, last completed bar
time, three exact upper deltas, previous/latest close and mid. Calculations
use the original context and completed-bar cutoff; no extra broker requests
are made. Diagnostic failures are logged without affecting output.

The observation does not change candidate group, readiness, entry/exit/stop,
ranking or Win~. It uses no future fall, incomplete candle or mandatory
RSI/MACD confirmation. A later price advance or renewed band acceleration
clears this particular observation. Other gates still apply. It neither
closes positions nor changes historical candidate/evaluation rows.

Native NVAX H4 upper increments are approximately 0.627 -> 0.335 -> 0.262
through October 6 04:00, whose confirmation is available only at 08:00.
The much flatter 08:00 bar is already the falling candle. Yahoo's displayed
13:00 rise must not be equated to a different native timestamp without
interval alignment. Two examples and focused checks do not establish
precision, recall or profitability. A match is a pause observation, not a
forecast of a decline. See `docs/investigations/2026-10/investigation-2026-10-15-nvax-p-pattern.md`.

User-run checks: `dotnet run --project tests/BellUpUpperBandChecks`.

Additional research hypothesis from the user (2026-10-15): during the
post-impulse bridge, track whether the rising upper band reaches/crosses
the flat candle level before price falls. Compare NVAX H4 October 5-6 with
ACVA H4 September 10-11 (`Data/Charts/ACVA-H4-Triangle.png`). In native cache,
NVAX October 6 04:00 close 12.43 remains above upper about 12.27346.
ACVA's first two plateau closes also remain above upper (10.43 vs 9.42912
and 9.98770); its third plateau close is 10.41 vs upper about 10.44294,
near the candle high 10.44, and the band subsequently moves above the plateau.
Thus being above upper on the second paused candle alone does not distinguish
these examples. Crossing at the third is unavailable at the second; a future
crossing cannot label the earlier decision. The small cache/high difference
also prevents claiming an exact wick intersection from rounded chart values.

Record this as a hypothesis for later diagnostics/research, not an admission
rule or proof that NVAX could not form a Triangle. The upper band is computed
from price: its subsequent flattening is partly a consequence of the falling
close, not proof the earlier band position caused the fall. Compare plateau
age, signed close/upper distance normalized by band width, candle body/wick
intersection, and continued upper growth on equal-age prefixes, including
continuations and failures. Existing pause diagnostics/filters are unchanged.

Cache study completed 2026-10-15:
`docs/investigations/2026-10/investigation-2026-10-15-pause-upper-position.md`. Primary H4 cohort
(one-bar rise, two paused bars, next three bars) contains 4,254 observations.
Down occurs in 30.9% when Upper < Low versus 29.5% when Upper has reached
Low or higher; ticker-cluster bootstrap difference interval includes zero.
Close–Upper distance AUC is 0.517. Later slices and alternate horizons do
not establish reliable direction separation. XP/QDEL continue upward with
Upper < Low; PCVX falls with Upper > High. Preserve diagnostic-only status;
do not turn upper/candle position into a trading veto from these examples.

Body refinement (2026-10-16): see
`docs/investigations/2026-10/investigation-2026-10-16-pause-body-position.md`. In the same primary
H4 cohort Down is 29.9% with Upper below body vs 31.1% inside body. This
does not establish safe admission on a body intersection. The user's proposed
sequence (last closed H4, then last closed D1), directional crossing event,
same-session horizon and mirrored lower-band case remain untested; neither
independent-timeframe table tests that full hypothesis. Keep diagnostic-only
status until a separately specified joint/prefix study provides evidence.

Impulse-level refinement (2026-10-16): the user distinguishes flat bodies
near the impulse Close (П/Л bridge) from upward steps whose bodies stay above
that Close (continuation context). Cache study:
`docs/investigations/2026-10/investigation-2026-10-16-pause-impulse-level.md`. With a long one-bar
rise and two paused H4 bars, next-bar Open–Close <=−5% occurs in 5.04% of
flat-at-top observations vs 2.17% of steps-above-top; sharp upside is also
more common in the flat group. At three paused bars the downside difference
nearly disappears. This is preliminary conditional risk separation, not a
validated П/BellUp classifier or guaranteed direction. FIG provides a
steps-above-top/Upper-inside-body counterexample followed by a >5% red H4.
No production filter or ranking change is justified by this study alone.

### Triangle Growth

Triangle is a diagnostic fallback, not a cross-timeframe veto. First classify
BellUp independently on Daily and H4; only if neither confirms BellUp may a
Triangle on either timeframe route the ticker to `Other`.

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
  - `H4` means today and may enter final `BellUp`
  - `Daily` means tomorrow and may enter final `BellUp`
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
- A higher-timeframe `BellUp` still needs H4 not to contradict it for today's
  trade-ready list. ReversalHook checks support at the next higher available
  scale: Daily context supports the recent-IPO H4 fallback; Weekly context
  supports a Daily hook, including the bounded
  [current-week recovery exception](REVERSAL_D1_WEEKLY_SUPPORT.md) for active
  DeepDeclineRecovery. Higher-scale support is a readiness check, not a
  substitute for the confirmed pattern.
- When the Daily BellUp geometry is strong but the long-lookback Daily mid is
  still descending, classify it as `DailyTransitionBellUp`. This is a
  diagnostic `Other` state, not a relaxed BellUp: retain it for outcome
  measurement and do not create a trade plan from it.
- Reject a Daily `BellUp` as post-factum when its row phase is already late:
  either the pattern appears only after H4 RSI and MACD histogram have rolled
  over from a local peak, or the Daily pattern already existed on the previous
  point while H4 RSI is elevated and the last two H4 rows show terminal band
  expansion. (This is the Daily-timeframe counterpart to the KMI H4
  terminal-pullback rejection above.)
  Since2026-10-23 the separate Daily-RSI-only rejection was removed following
  SPT: RSI80.04->77.99 cannot alone mark continuing Daily expansion as Late
  phase, even if H4 has cooled. The two combined branches above remain.
  Daily RSI stays in diagnostics/other existing model inputs; no new80/20
  hard gate or probability recalibration is implied. The removed Daily-RSI
  bypass branch is also gone; fresh H4 continuation still serves the separate
  Daily recent-boost override. BellUpLatePhaseChecks covers SPT-like admission
  through this guard and retained combined-rejection controls. It is not a
  full SPT plan/outcome replay.
- After loading the current M15 rows, reject a BellUp candidate when its latest
  closed H4 close was above the H4 Bollinger mid but live M15 price has crossed
  below that same mid. This is a structural invalidation of the saved setup,
  not a fixed percentage-move filter.
- Use M15 only for execution timing after D1/H4 classification. For `BellUp`,
  a confirmed M15 `BellUp` predicts entry near the rising M15 mid or the latest
  shallow pullback low. For `ReversalHook`, a confirmed M15 hook predicts
  entry near the hooked lower band or the latest local low. If the matching
  M15 pattern is unavailable, retain the existing entry forecast as fallback;
  M15 must not change the Daily/H4 pattern classification.

The final `BellUp` list currently admits only AMLX-like `BellUp` candidates on
real Bollinger rows from `H4` or `Daily`. (The template-match ranking support
and low-amplitude veto mentioned in older notes were removed 2026-09-01 — see
`SCANNER_MODEL.md` "Series-template matching"; the veto had never actually
fired in the feature's history.) Weekly-only Bell and non-Bell continuation
shapes are diagnostic context, not final `BellUp` promotion.

When strict promotion returns no playable candidates, keep the result empty;
preserve diagnostic rows under `Other` when available.

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
  `BellUp` path even if an older live-mover or template
  branch likes it.

When comparing band curves across tickers, compare shape rather than absolute
price. Normalize by the starting point or compare deltas from the first point.

## BellUp entry timing (user clarification, 2026-09-09)

The active objective is a playable `BellUp` list,
at the right moment before the boost. Recognizing the BellUp shape and
deciding whether entry is already late are separate checks. Setups matching
neither playable family belong in `Other`.

### Current timing rule (2026-10-07)

On the confirmed BellUp timeframe (Daily or H4), T-1 is the latest completed
candle. It blocks entry as Recent boost when Close > Open and its High-Low
range is at least the configured multiplier times the median range of the
previous N completed candles. Defaults: N=5 and multiplier=2. The current
candle is excluded from the median. An older impulse followed by ordinary
consolidation does not veto entry by itself.

The selected-timeframe check and the existing confirmed-Daily cross-timeframe
veto use this same rule. Existing H4 continuation overrides and other readiness
checks remain in force. Only closed candles establish the timing veto; Daily
completion is 16:00 and H4 completion is start+4h in market-local timestamps.
Native D1 is preferred, with H4-derived Daily bars as the existing fallback.

The check needs N+1 completed candles (six by default). Invalid settings,
negative ranges or a non-positive median produce an explicit non-ready reason.
A veto reports timeframe, T-1 time, range, prior median, ratio, window and
threshold. Missing-history rejection is not a boost.

`BellUpEntryTiming.IsReady` implements range timing. Its separate legacy
`IsBoost(candle, preceding)` remains a body-based predicate solely for
`BellUpBoostExit`: target selection/averaging still uses prior boost bodies.
This entry-timing change does not replace exit targets with wick ranges.
Final BellUp target reduction and minimum-profit rules remain applicable.

Settings: GetCandidates.TradePlan.BellUpRecentBoostRangeLookbackBars and
BellUpRecentBoostRangeMultiplier. This user-authorized experimental change is
supported by the dated range study, not a full trade-plan holdout evaluation.
It does not guarantee admission if another readiness check fails.

Historical rule through 2026-10-06: green T-1 body >=2x absolute T-2 body,
with three completed bars required. The dated SECZ notes below describe that
old body rule and remain investigation history.

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
The active timing rule therefore vetoes T-1 only; do not add a numeric age
window until the chart labels and outcomes are measured together. A later Yahoo chart refresh showed the ordinary H4 grid
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
(that mechanism is removed — see "Practical use and template sources" above).
The existing sort combines qualityScore * 50 with legacy NextDayRank;
DiagnosticsEstimatedHitRatePct is a separate confidence estimate. Validate
changes against [current success criteria](../../../guides/success-criteria.md),
not historical amplitude thresholds alone.
