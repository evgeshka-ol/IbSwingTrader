---
name: ibswingtrader-scanner
description: Use when working on the scanner, candidate ranking, wishlist promotion, TodayResearchLike vs Reversal separation, or tuning scanner settings in IbSwingTrader. Covers how the project selects candidates, how to read the main series, where the scanner logic lives, and which settings and outputs matter most.
---

# IbSwingTrader Scanner

Use this skill when changing scanner behavior or analyzing why a ticker was or was not surfaced by `get-candidates`.

## Responsibility boundary

Codex only analyzes data/code, changes code, and creates or updates documentation from that analysis.

The user runs builds, the application, scanner/research/evaluation commands, and tests. Do not try to launch them unless the user explicitly asks to change this rule.

## Core intent

The scanner's job is to find future fat moves early.

- Minimal near-term goal: the #1 current `Runaway` row should
  consistently become a practical winning idea and capture more than 10%.
  Optimize top-1 quality before widening attention to the rest of the list.
- For scanner quality, the main oracle is `AmplitudePct`, not `Win/Loss/NoEntry`.
- `NoEntry` may be a `TradePlan` problem.
- Low amplitude is a scanner problem.
- Priority #1: names in today's summary `Runaway` should be confirmed by later evaluation as high-amplitude winners.
- Series shape is a primary scanner signal. The feature row must always come from the saved scanner snapshot. Evaluation supplies only the outcome label and amplitude.

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

## Main split

- `Reversal`: below-mid / pullback / return-to-mean style ideas.
- `Runaway`: above-mid / runaway / continuation style ideas.

Internal `Runaway` subtypes:

- `BellUp`: squeeze-to-launch continuation, the canonical direct form
- `Runaway`: strict kink/acceleration launch
- `LaunchContinuation`: real Bollinger launch with strong daily/H4 expansion
- `PullbackContinuation`: constructive continuation after a pullback

Current final `Runaway` admission is intentionally strict: the candidate must
confirm `BellUp` on real Bollinger rows in either `H4` or `Daily`. A positive
high-amplitude template match supports ranking and confidence but is not a hard
admission requirement. A closer low-amplitude BellUp match is a promotion veto.
Other above-mid continuation subtypes remain diagnostic only.

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

Current final `Reversal` promotion uses the working `ReversalHook` pattern
after the hard split. The split only decides that the ticker is below the daily
Bollinger mid; `ReversalHook` decides whether it is a trade-ready return setup.
The hook decides admission. A real D1 or H4 match to a high-amplitude
(`AmplitudePct >= 10%`) reversal template supports ranking and confidence but
is not a hard admission requirement. Weekly rows remain context only.
The hook is detected on real daily rows:

- lower Bollinger band broke down and then hooks upward
- the lower-band turn must be fresh: the transition from a negative delta to
  non-negative deltas must still be visible in the last three daily points
- daily mid is still weak but the downward move is decelerating or turning
- band width is compressing after the breakdown
- MACD histogram is still weak/negative but turns upward toward zero
- MACD line and signal are converging
- RSI is recovering from the recent low

Good `ReversalHook` examples include POET, ASM, SSRM, CDE, and SVM from the
2026-06-15 evaluation set. The ideal entry is usually the first or second daily
bar after the lower-band hook; later scans may still work but are less clean.

## Series-template direction

When improving scanner recall, promotion, or ranking, prefer a literal series
similarity signal before adding more derived heuristics.

- Compare rows point-by-point with small tolerance, not only by computed slopes.
- Normalize comparable series from their first point so shape matters more than absolute level.
- Calculate Daily and H4 similarity independently. A match on either timeframe
  is sufficient; do not average their distances. Weekly rows are context only
  and must not decide template admission or final promotion.
- Never use feature rows from `research_top_gainers.csv` or `evaluation-dataset.csv` as scanner templates; those rows may include bars observed after the original scan.
- Join evaluation labels back to `candidates.csv` by ticker, preset scan code, and exact scan time.
- Use `AmplitudePct >= 10%` plus the confirmed family pattern as the positive label.
- Use the matching low-amplitude range plus the confirmed family pattern as the negative label.
- Trade-plan outcome is not part of this template label.
- Give extra weight to higher-amplitude template matches when the geometry is otherwise similar.
- For `Reversal`, use only high-amplitude reversal rows from the evaluation dataset.
- A candidate close to historical winner templates should get promotion/ranking support.
- A candidate matching a low-amplitude BellUp template at least as closely as
  its positive winner template must be rejected during promotion.
- If a scan has many candidates above `AmplitudePct >= 10%`, treat them as
  the playable pool and learn low-amplitude rejection from rows below 10%.
  The goal is to remove the low-amplitude third by similarity to today's
  low-amplitude report rows, not by broad one-size-fits-all thresholds.
- If you need to reject weak candidates before evaluation knows the true
  `AmplitudePct`, do it late and only through row-based envelope expansion
  proxies. Do not hard-cut the family split or Bell classification.
- If strict promotion leaves both final families empty, return an empty result.
  Do not disable the low-amplitude template veto to manufacture a candidate:
  that re-admits rows which the evaluation feedback has already identified as
  weak and damages top-1 quality.

### Current ranking implementation (2026-07-11)

The literal series-template signal above is no longer just an additive bonus
sitting alongside ~30 unrelated heuristic proxies. `ReRankCandidates` in
`CandidateFinder.cs` now sorts by a strict tier first:

- `Confirmed` (close template match, no low-amplitude veto) always outranks
  `Weak` (looser match), which always outranks `None` (no match, or vetoed by
  a closer low-amplitude template match).
- Inside a tier, the matched template's realized `AmplitudePct` is the primary
  sort key — this is the literal "sort descending by predicted amplitude"
  behavior. The legacy heuristic `NextDayRank` score only breaks ties within
  the same tier and the same (rounded) template amplitude.
- Both `Runaway` and `Reversal` now rerank across their entire candidate pool
  for a given scan, not just a capped top window; `Reversal` previously left
  roughly the bottom half of a large list unadjusted by template matching.
- `candidates.csv` now exposes `Diagnostics.TemplateRankTier`,
  `SeriesSimilarityTemplateTicker/Family/Bonus`, and
  `LowAmplitudeTemplateTicker/Penalty` per row, so a ranking outcome can be
  audited directly instead of inferred from raw indicator series.

`FullMatchBonus`/`WeakMatchBonus`/`LowAmplitudePenaltyWeight` still exist for
the diagnostic `SeriesSimilarityBonus`/`LowAmplitudePenalty` values shown in
the CSV, but no longer move the final rank by themselves; `FullMatchDistance`
decides the `Confirmed`/`Weak` boundary and `WeakMatchDistance` decides whether
there is a match at all.

## Bollinger pattern direction

Bollinger band shape patterns are timeframe-scalable. Do not treat them as
daily-only signals.

- `Weekly` Bollinger launch: broad background and rare large swing potential.
- `Daily` Bollinger launch: main swing/next-day potential, as in TE-style moves.
- `H4` Bollinger launch: early trigger, intraday capture, or next-day
  continuation, as in ONDS-style moves.

The same upper/mid/lower band pattern can be useful on any available timeframe,
but the trade decision changes with timeframe. Use the real band series
(`*BbUpperBandSeries`, `*BbMidBandSeries`, `*BbLowerBandSeries`) to detect the
shape, then use timeframe context to decide ranking strength and trade profile.

Canonical Bell pattern pair:

- `BellUp`: squeeze, launch, then late flattening/mean-reversion warning on the direct bullish form
- `BellDown`: mirrored squeeze, launch down, then late flattening/mean-reversion warning on the bearish form

`BellUp` belongs to the above-mid continuation side and should be promoted when
the rows show a squeeze-to-expansion launch. In code, that can be recognized
either by a broader phase comparison or by a short local turn where the upper
and mid Bollinger rows bend up together and band width starts opening again.
The opening must be material, not a nearly parallel upward translation of all
three bands. Self-matching against the same ticker is not valid positive
template evidence.
`BellDown` is the mirrored form used on the below-mid reversal side.

The scanner matches Bell only on H4 and Daily. One clean matching timeframe is
enough:

- `H4`: eligible for final `Runaway`, play it today
- `Daily`: eligible for final `Runaway`, play it for tomorrow
- `Weekly`: background context only; it is not passed to the Bell matcher

The source timeframe changes urgency and trade-plan depth, not the family split.

When the same runway pattern appears, split it by phase using only the saved
pre-move rows:

- `ReadyNow`: H4 real Bollinger trigger confirms the launch. The upper band
  expands upward, mid is not falling, lower band is not simply being dragged
  upward, and H4 RSI/MACD do not contradict the trigger. These candidates can
  be promoted to `Runaway`.
- `NotReady`: Daily/weekly runway shape exists, but H4/Daily trigger is not
  trade-ready. Reject it from the current final list. Do not force it into
  `Reversal`.
- `Neutral`: the saved rows do not confirm a trade-ready runway phase. Do not
  let legacy live-mover/template/bypass branches promote it into
  `Runaway`.
- Reject a Daily `BellUp` as post-factum when its row phase is already late:
  either the pattern appears only after H4 RSI and MACD histogram have rolled
  over from a local peak, or the Daily pattern already existed on the previous
  point while H4 RSI is elevated and the last two H4 rows show terminal band
  expansion.
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

This split is important for same-pattern candidates: UMAC/ONDS-like rows are
ready for immediate `Runaway` admission, while SHLS-like rows with only a
higher-frame setup are rejected until a future scan finds a trade-ready
H4/Daily pattern.

Broader series direction: move pattern logic toward the visual indicators the
user actually relies on: Bollinger, MACD, and RSI. The priority order for
scanner prediction, filters, promotion, and ranking is:

1. real Bollinger upper/mid/lower curves
2. real MACD line, signal line, and histogram
3. real RSI as a confidence/ambiguity correction

MA rows and older distance/width rows are legacy/context. Do not base new
prediction logic on them unless a concrete analysis proves they add value
beyond the real Bollinger/MACD/RSI rows.

Current cleanup rule is stricter: scanner similarity and generated candidate
series output contain only the real Bollinger upper/mid/lower, MACD
line/signal/histogram, and RSI rows. Do not reintroduce MA, distance, width,
MACD aliases, weighted timeframe totals, or stored slope summaries.

## Main code

- Scanner core: `IbSwingTrader/Application/Candidates/CandidateFinder.cs`
- Ranking/tiering: `ReRankCandidates` / `ResolveTemplateRankTier` in `CandidateFinder.cs`
- Output writer: `IbSwingTrader/Infrastructure/Logging/CandidateResultWriter.cs`
- CSV column export (incl. ranking diagnostics): `IbSwingTrader/Infrastructure/Logging/CandidateCsvRowBuilder.cs`
- Settings: `IbSwingTrader/agentsettings.json`
- Command: `IbSwingTrader/App/Commands/GetCandidatesCommand.cs`

## Read these references

- `references/PROJECT_OVERVIEW.md`
- `references/SCANNER_MODEL.md`
- `references/SERIES_PLAYBOOK.md`
- `references/SETTINGS_MAP.md`

## Practical workflow

When scanner quality is weak:

1. Check whether the ticker was missed by the market presets, rejected by the family pattern, or present but ranked too low.
2. Use the original series in `candidates.csv`; use evaluation only to label those snapshots.
3. For a ranking question specifically, read `TemplateRankTier`,
   `SeriesSimilarityTemplateTicker`, and `SeriesSimilarityBonus` on the
   candidate directly from `candidates.csv` before guessing: `None` means no
   winner template matched (or a low-amplitude template vetoed it), which
   points at recall/matching, not at the tier/sort mechanism itself.
4. Prefer fixing:
   - recall
   - family pattern recognition after the hard daily split
   - ranking
5. Touch `TradePlan` only after the list is already good.
