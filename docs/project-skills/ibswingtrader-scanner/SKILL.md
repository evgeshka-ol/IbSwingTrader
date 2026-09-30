---
name: ibswingtrader-scanner
description: Use when working on scanner classification, BellUp or ReversalHook readiness, candidate ranking, wishlist promotion, or scanner settings in IbSwingTrader. Covers how the project selects candidates, how to read the main series, where scanner logic lives, and which settings and outputs matter most.
---

# IbSwingTrader Scanner

Use this skill when changing scanner behavior or analyzing why a ticker was or was not surfaced by `get-candidates`.

## Responsibility boundary

Codex only analyzes data/code, changes code, and creates or updates documentation from that analysis.

The user runs builds, the application, scanner/research/evaluation commands, and tests. Do not try to launch them unless the user explicitly asks to change this rule.

## Core intent

The scanner's job is to find future fat moves early.

- **Current output contract (since 2026-09-23):** candidates are classified
  into `BellUp`, `ReversalHook`, or `Other`. This replaced the former
  `Runaway`/`Reversal` Daily-mid family split as the candidate-group model.
  Legacy fields and historical datasets may still contain the old names; do
  not treat them as current output groups. See `references/SETTINGS_MAP.md`
  for CSV compatibility notes.
- BellUp remains the immediate production focus. ReversalHook is evaluated
  independently of whether price is above or below the Daily mid; its own
  pattern and higher-timeframe readiness checks determine playability.
  Confirmed but non-ready patterns are retained in `Other`. Triangle is a
  diagnostic fallback and must not override BellUp on the other timeframe.
- Keep pattern recognition separate from trade readiness. BellUp has
  timeframe-specific timing and phase checks. ReversalHook confirmation uses
  Daily rows, with H4 as a history fallback for recent IPOs. Pattern
  classification alone does not imply a successful trade.
- The scanner seeks future high-amplitude moves. `AmplitudePct` is the
  scanner-quality oracle; trade-plan conversion is evaluated separately.
  Use current research/evaluation feedback rather than assuming older
  `Runaway`-specific rankings still describe current groups.
- For scanner quality, the main oracle is `AmplitudePct`, not `Win/Loss/NoEntry`.
- `NoEntry` may be a `TradePlan` problem.
- Low amplitude is a scanner problem.
- Priority #1: names in today's playable `BellUp` summary should be
  confirmed by later evaluation as high-amplitude winners.
- Series shape is a primary scanner signal. The feature row must always come from the saved scanner snapshot. Evaluation supplies only the outcome label and amplitude.

## Current architecture and output contract (2026-09-29)

- Broker authentication remains entirely inside TWS/IB Gateway. The scanner
  connects through the local IB API socket and contains no broker credentials
  in source or settings.
- TWS is the canonical source for historical bars. The cache stores per-ticker
  series by timeframe; H4 analysis uses the native TWS grid (normally
  `04:00`, `08:00`, `12:00`, `16:00` in the exchange timezone), not Yahoo's
  sometimes irregular session-anchored bars. Missing history is requested from
  TWS and merged into the cache before feature extraction.
- `candidates.csv` puts scalar trade fields first (`ScanPrice`, `EntryPrice`,
  `ExitPrice`, `StopLoss`, `StopLimitPrice`, planned percentages, and
  `ExitProfile`). `PatternVerdictReason` follows `CandidateGroup` and contains
  values such as `BellUp confirmed on H4` or `BellUp confirmed on Daily`; the
  technical OHLC/indicator arrays are intentionally at the end of the row.
- The three output sections and CSV `CandidateGroup` values are `BellUp`,
  `ReversalHook`, and `Other`. Only the first two are playable groups;
  `Other` retains rejected, unconfirmed, or non-ready diagnostics.
- `Triangle` is now a distinct diagnostic pattern, not a trading category. The
  experimental scanner heuristic is checked on H4 and Daily: a positive body
  at least twice the prior body followed by several small, tightly clustered
  closes is treated as post-spike consolidation and routed to `Other` rather
  than admitted as BellUp. It also recognizes a short multi-candle rise when
  the later plateau is quiet and the Bollinger envelope has contracted from
  its post-rise maximum; a fresh late expansion remains BellUp-like. This is
  deliberately experimental and must be validated against new scans before
  thresholds are tightened. It is evaluated only after BellUp has been checked
  on both H4 and Daily; a Triangle on one timeframe must not hide a confirmed
  BellUp on the other.
- `DailyTransitionBellUp` is a separate diagnostic state: the Daily BellUp
  geometry is strong but the longer Daily-mid direction remains `Down`. It is
  emitted to `Other`, never admitted or ranked as `BellUp`; collect its
  realized outcomes before considering a production admission rule.
- `ReversalHookPreparing` is likewise diagnostic-only: price remains below
  the closed Daily mid but turns toward it while both the MACD histogram and
  MACD line/signal gap confirm a turn. The lower Daily band may still be
  falling and BB width may still be expanding; both lag an impulsive reversal.
  It is emitted to `Other` without a plan; it must earn any future admission
  from realized outcomes.
- BellUp timing rejects a setup only when the latest qualifying boost has
  occurred on the confirmed timeframe. A T-2 impulse followed by a completed
  consolidation candle remains eligible. Additionally, when Daily
  independently confirms BellUp, its timing is a cross-timeframe veto even
  for an H4-selected signal; an H4-only BellUp is not blocked by absent Daily
  confirmation. H4 lower-band turn with a flat Daily middle band is an
  exhaustion veto. BellUp exits use the average body of two prior
  qualifying impulse candles when available, or the single prior impulse when
  that is all the cache provides.

## Pattern-first groups (implemented 2026-09-23)

The former `Runaway`/`Reversal` split was too dependent on the latest Daily
Bollinger mid. BBNX demonstrated the failure mode: a ReversalHook can begin
below the mid, cross above it, and then produce a large continuation. The
pattern-first groups were implemented in commit `d4a4f1d`:

- `BellUp`: a confirmed BellUp pattern on H4 or Daily;
- `ReversalHook`: a confirmed Daily ReversalHook, with H4 as a fallback for
  recent IPOs that lack reliable Daily history;
- `Other`: neither pattern confirmed or a confirmed pattern rejected as not
  currently trade-ready.

The live console and CSV use these groups. Candidate processing uses one pool
until pattern classification is complete; the former Daily-mid split no
longer defines the output category. BellUp is checked before ReversalHook. A
detected BellUp that fails timing or readiness remains `Other`, rather than
being relabeled ReversalHook. ReversalHook playability also requires
higher-timeframe support.
The ReversalHook implementation was subsequently strengthened to check
support from the next higher available scale; the check can accept either a
higher-scale BellUp or an early return-to-mid context. A Daily hook checks
Weekly context, while the H4 IPO fallback checks Daily context.

Pattern episodes should eventually retain timeframe and phase metadata (`Active`,
`Exhausted`, or `Transition`) and, where available, the previous pattern. A
useful transition shape is `ReversalHook -> BellUp -> Exhausted`; BBNX is the
first exemplar for this state model: Daily ReversalHook around Aug 3-7, a
short H4 ReversalHook around Aug 3-4, H4 BellUp beginning Aug 7 and exhausting
after the Aug 11 impulse, plus a later Daily ReversalHook beginning Sep 14.
This episode-history model is a future direction; current output stores the
detected group and confirmation reason, not a complete transition history.

BBNX remains an exemplar, not a hard-coded template. The group migration is
live; further ReversalHook detection or ranking changes still need more
cache-confirmed examples and empirical validation against realized outcomes.

## Pipeline

1. `get-candidates`
2. inspect `Data/Tickers/candidates.csv`
3. compare yesterday's scan with today's `research_top_gainers.csv`

The scanner no longer uses `wishlist.csv` as an intermediate candidate stage.
Historical candle accumulation belongs to the historical cache, not to a
second candidate queue.

For H4 history, load missing chunks newest-first. A recently listed ticker may
have no data at the old edge of the requested lookback while still having
enough recent H4 bars for scanner analysis. The first empty older chunk after
valid data marks the listing boundary and must not discard the loaded bars.

## Legacy family model (historical context)

The following `Runaway`/`Reversal` family split describes the former model
and legacy fields. It is retained to explain historical datasets and older
analysis notes; it does not define current candidate output groups.

- `Reversal`: below-mid / pullback / return-to-mean style ideas.
- `Runaway`: above-mid / runaway / continuation style ideas.

Internal `Runaway` subtypes:

- `BellUp`: squeeze-to-launch continuation, the canonical direct form
- `Runaway`: strict kink/acceleration launch
- `LaunchContinuation`: real Bollinger launch with strong daily/H4 expansion
- `PullbackContinuation`: constructive continuation after a pullback

The former final `Runaway` admission was intentionally strict: the candidate
had to confirm `BellUp` on real Bollinger rows in either `H4` or `Daily`. (The
literal-template match/veto mentioned in older notes was removed 2026-09-01 —
see "Series-template direction" below.) Other above-mid continuation subtypes
remain diagnostic only.

Do not mix the two mentally or in code. They are opposite regimes and need different ranking logic.

Hard classification rule:

- The latest completed daily Bollinger mid bar is the primary hard split.
- Infer the expected latest closed D1 date from the dominant latest date in
  the current market-wide D1 data. Do not assume that the previous weekday was
  a trading day; exchange holidays must not make every family split unknown.
- If the ticker was below the daily Bollinger mid on the last closed daily bar, it belongs to `Reversal`.
- If the ticker was at or above the daily Bollinger mid on the last closed daily bar, it belongs to `Runaway`.
- For a recent IPO without enough reliable D1 rows, use H4 close versus H4 mid
  as a scoped family fallback and detect `ReversalHook` on H4 rows. Do not use
  this fallback when sufficient D1 history exists.
- Weekly and H4 context only refine subtyping, promotion, and ranking inside the family.
- Do not retune this family split when trying to improve list quality; keep the category boundary fixed and work only on promotion, ranking, and trade-plan behavior after the split.

Both families should produce meaningful future amplitude:

- `Runaway` should be the strongest next-day research proxy.
- `Reversal` should still usually produce `AmplitudePct > 10%`.
- If `Reversal` amplitude is below 10%, treat that as scanner failure, not a trade-plan issue.

Current final `ReversalHook` promotion uses the ReversalHook pattern without
the former hard below-mid split. See the current output contract above and
`SERIES_PLAYBOOK.md` for current readiness rules.
Weekly rows remain context only. Full row-shape checklist (incl. the
freshness requirement on the lower-band turn) and worked examples:
`SERIES_PLAYBOOK.md` "ReversalHook" — do not restate or fork that checklist
here.

**2026-09-01 finding: this gate carries no measurable signal.** Every one of
`IsReversalHookPattern`'s 14 boolean sub-flags, tested individually against
194-260 decided Win/Loss `DiagnosticRejected` Reversal rows in
`evaluation-dataset.csv`, came back AUC 0.43-0.54 (noise); the compound gate's
pass/fail count has no monotonic relationship with outcome either.
`CalculateReversalHookQualityScore` itself: AUC 0.51. See
`references/REVERSAL_EDGE.md` "Open items" for the full result and the
continuous-feature sweep that followed it. Per the user's explicit choice,
this is not being fixed by retuning the gate/score (nothing here has been
shown to carry signal to retune) — instead raw Daily/H4 OHLC candle series
are meant to be captured (`RecentDailyOpenSeries/HighSeries/LowSeries`,
`RecentH4OpenSeries/HighSeries/LowSeries/CloseSeries`, added 2026-09-01) so
the candle-level mechanics described in `REVERSAL_EDGE.md`'s `NAT` example
can eventually be tested directly. **Correction (2026-09-03): this capture is
not actually happening in `evaluation-dataset.csv` in practice** — checked
across all 2925 rows, these OHLC fields are non-empty in only ~4.8% (140
rows), all sharing one single `ScanTime` batch, not the normal per-scan flow;
everywhere else only the derived Bollinger/MACD/RSI series are present. Do
not assume raw candles are available from this CSV — reconstruct them from
`Data/cache/<TICKER>.json` instead (see `REVERSAL_EDGE.md` and
`tools/Backfill-ReversalCandles.ps1`) until the write-path gap is actually
fixed.

**2026-09-03 architecture decision:** the binary `IsReversalHookPattern` gate
is being replaced, not retuned — see `references/REVERSAL_EDGE.md` "Open
items" for the full reasoning, the `NAT` case that motivated it (a real
intraday capitulation-drop entry the daily-mid gate structurally cannot see),
and how this differs from the already-disproven July series-similarity
approach. Two pieces, both still unimplemented/unvalidated:

1. A layered timeframe funnel, in this exact role order: **Weekly** = macro
   risk gate only ("safe to hold overnight" — not a pattern check). **Daily**
   = confirm or defer the setup ("play it" vs "not today"). **H4** = same-day
   vs today-into-tomorrow timing. **M15** = pure execution (entry/exit/stop
   numbers), never a go/no-go layer.
2. Admission scored as **graded similarity to a small, hand-curated exemplar
   chart library**, validated by AUC against `evaluation-dataset.csv` before
   being wired in — not a binary threshold/gate.

Apply the same rigor as everywhere else in this file: nothing here gets
wired into admission or ranking until it clears an AUC/holdout/asymmetry bar.

`Reversal` is the user's own years-proven manual trading edge (this app's job
is to remove emotion and scale past the broker scan API limit, not to
discover the pattern) and should get more attention than `Runaway` tuning
when both need work — see `references/REVERSAL_EDGE.md` for why, the precise
"mid band bends like a hypotenuse" trigger definition, the signal hierarchy,
a full worked example, and open items that were investigated but
deliberately left unimplemented pending more evaluation data.

## Series-template direction — removed 2026-09-01

Literal series-similarity template matching (`ApplySeriesSimilarityDiagnostics`,
`CalculateSeriesSimilarityMatch`, `LoadSeriesSimilarityTemplatesAsync`,
`ResolveTemplateRankTier`, the `SeriesTemplateRankTier` enum, and the
`Diagnostics.SeriesSimilarityTemplateTicker/Family/Bonus`,
`LowAmplitudeTemplateTicker/Penalty`, `TemplateRankTier` columns in
`candidates.csv`) has been **deleted from the codebase**, not merely demoted.
It had already been reduced to an audit-only side-channel on 2026-07-13 (see
`SCANNER_MODEL.md` "Ranking implementation status") after being disproven as a
ranking driver; this session found the audit data itself was never
informative either — across the entire history of `candidates.csv` since that
diagnostic column started being written (2306+ rows), `TemplateRankTier` was
`"None"` in 100% of them, meaning the low-amplitude admission veto that used
to sit inside `IsTodayResearchLikeCandidate` could structurally never have
fired. Both were removed together. `SeriesTemplateFamily` the enum still
exists and is still used, but purely as a plain discriminator to pick
`CalculateRunawayLaunchQualityScore` vs `CalculateReversalHookQualityScore` —
it no longer drives any template matching. `SERIES_PLAYBOOK.md`'s old
"Template sources"/"Practical use" sections have been replaced with a pointer
to this history; do not resurrect that methodology without new evidence.

### Ranking (current state — see `SCANNER_MODEL.md` for validation history)

`ReRankCandidates` in `CandidateFinder.cs` ranks the BellUp pool with the
Runaway-era quality formula and the remaining pool (ReversalHook plus
diagnostics) with the Reversal-era formula. Those formula names and AUC
validation cohorts predate the pattern-group migration; see `SCANNER_MODEL.md`
for historical results. Rejected rows remain available under `Other` (see
`SETTINGS_MAP.md` `EmitAllSeenCandidates`).

- `CalculateRunawayLaunchQualityScore` (legacy Runaway formula, used for BellUp): Daily mid/upper-band tail
  slope + H4 mid-band tail slope + H4 upper-band tail slope (added
  2026-09-01, AUC 0.673 alone) + H4 band-width expansion (added 2026-09-01,
  AUC 0.639 alone) — combined AUC 0.62 → 0.69 on 422 decided rows, stable
  across a chronological split. See `SCANNER_MODEL.md` for the full feature
  sweep and rejected candidates (Weekly slopes, RSI, MACD histogram all
  tested weaker).
- `CalculateReversalHookQualityScore` (legacy Reversal formula, used for the remaining pool): Daily band-width
  compression + Daily lower-band hook tail slope. Re-tested 2026-09-01 against
  260 decided rows: AUC 0.51, no signal — see `REVERSAL_EDGE.md` "Open items"
  before extending this formula further.
- Sort key: `AdjustedRank = qualityScore * 50 + legacy NextDayRank`. The
  series-template tier/distance machinery described in older notes is gone
  (removed 2026-09-01, see above) — check `RankingQualityScore` to explain
  why one candidate outranks another.

Bell/ReversalHook classification (envelope math, curve-turn checks,
vertical-spike detection) lives in one shared place, `BellPatternClassifier`
(`Application/Candidates/BellPatternClassifier.cs`), used by both the live
scan (`CandidateFinder.cs`) and offline evaluation
(`CandidatePatternVerdictService.cs`). Change a rule there once, not in
either caller — the two paths silently drifted apart before this
consolidation.

**Before adding or reweighting any ranking feature, validate its AUC against
realized `AmplitudePct` in `evaluation-dataset.csv` first.** This is exactly
the discipline that caught the 2026-07-11 series-template tiered-sort attempt
not working — see `SCANNER_MODEL.md` "Ranking implementation status" for
that history and the current AUC numbers per feature; don't re-run that
experiment.

## Bollinger pattern direction

Bollinger band shape patterns are timeframe-scalable (Weekly = broad
background, Daily = main swing/next-day, H4 = early trigger/intraday) — do
not treat them as daily-only signals. Use the real band series
(`*BbUpperBandSeries`, `*BbMidBandSeries`, `*BbLowerBandSeries`); full
per-timeframe interpretation and worked examples (TE, ONDS) are in
`SERIES_PLAYBOOK.md` "Bollinger Squeeze Launch".

### Band-kink / RSI-rollover (2026-08-11) — operative summary

`IsLateBellUpPhase` includes a Daily-RSI-rollover-from-peak check
(`recentDailyRsiPeak >= 70m && dailyRsi[^1] < dailyRsi[^2]`, validated
AUC=0.61 — comparable to the slope features already driving
`RankingQualityScore`). **Do not add a standalone "any band slope kink"
filter on top of this** — tested independently of RSI, that piece came back
at chance (AUC=0.54 on the upper/mid kink itself, AUC=0.48 on the
`DKNG`/`ChannelReclaim` lower-band variant). Full investigation, the
HELP/DKNG grounding examples, and why `ChannelReclaim` still needs raw H4
candle data before it can be tested are in `SCANNER_MODEL.md`.

`BellUp` (bullish squeeze→launch) and `BellDown` (mirrored bearish form) are
recognized from real Bollinger upper/mid/lower rows on H4 or Daily only
(Weekly is context, never passed to the Bell matcher). Full geometry,
counter-examples, the `ReadyNow`/`NotReady`/`Neutral` phase split, the
Daily-post-factum rejection rule, and the M15 structural-invalidation /
execution-timing rules are all canonical in `SERIES_PLAYBOOK.md` — do not
restate or fork that checklist here.

Signal priority for scanner prediction/filters/promotion/ranking: real
Bollinger upper/mid/lower curves first, real MACD line/signal/histogram
second, real RSI third as a confidence correction. MA and width/distance rows
are legacy context only — see `SERIES_PLAYBOOK.md` "Primary series" for the
migration rule and what `candidates.csv` currently admits.

## Main code

- Scanner core: `IbSwingTrader/Application/Candidates/CandidateFinder.cs`
- Ranking: `ReRankCandidates` (quality-score based; no template tiering
  anymore) in `CandidateFinder.cs`
- Shared Bell/ReversalHook pattern classifier (used by both the live scan and
  offline evaluation): `IbSwingTrader/Application/Candidates/BellPatternClassifier.cs`
- Output writer: `IbSwingTrader/Infrastructure/Logging/CandidateResultWriter.cs`
- CSV column export (incl. ranking diagnostics): `IbSwingTrader/Infrastructure/Logging/CandidateCsvRowBuilder.cs`
- Settings: `IbSwingTrader/agentsettings.json`
- Command: `IbSwingTrader/App/Commands/GetCandidatesCommand.cs`

## Read these references

- `references/PROJECT_OVERVIEW.md`
- `references/SCANNER_MODEL.md`
- `references/SERIES_PLAYBOOK.md`
- `references/SETTINGS_MAP.md`
- `references/REVERSAL_EDGE.md`

## Practical workflow

When scanner quality is weak:

1. Check whether the ticker was missed by the market presets, rejected by a
   pattern/readiness check, or present but ranked too low.
2. Use the original series in `candidates.csv`; use evaluation only to label those snapshots.
3. For a ranking question specifically, read `RankingQualityScore` and
   `EstimatedHitRatePct` on the candidate directly from `candidates.csv`
   before guessing (the old `TemplateRankTier`/`SeriesSimilarity*` diagnostic
   columns were removed 2026-09-01 along with the feature they described).
4. Prefer fixing:
   - recall
   - BellUp/ReversalHook pattern recognition and readiness
   - ranking
5. Touch `TradePlan` only after the list is already good.
