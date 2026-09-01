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
after the hard split — the split only decides "below daily mid";
`ReversalHook` decides trade-readiness and is what actually gates admission.
A real D1/H4 match to a high-amplitude (`AmplitudePct >= 10%`) reversal
template supports ranking/confidence but is not a hard requirement. Weekly
rows remain context only. Full row-shape checklist (incl. the freshness
requirement on the lower-band turn) and worked examples: `SERIES_PLAYBOOK.md`
"ReversalHook" — do not restate or fork that checklist here.

`Reversal` is the user's own years-proven manual trading edge (this app's job
is to remove emotion and scale past the broker scan API limit, not to
discover the pattern) and should get more attention than `Runaway` tuning
when both need work — see `references/REVERSAL_EDGE.md` for why, the precise
"mid band bends like a hypotenuse" trigger definition, the signal hierarchy,
a full worked example, and open items that were investigated but
deliberately left unimplemented pending more evaluation data.

## Series-template direction

Prefer literal series-similarity as **supporting/ranking evidence**, never as
a hard admission requirement. Full comparison methodology, template sourcing,
and veto mechanics are canonical in `SERIES_PLAYBOOK.md` ("Template sources",
"Practical use") — do not restate or fork that checklist here.

Two rules worth keeping at quick-access level, because getting them wrong
silently corrupts every template built afterward:

- **Never use feature rows from `research_top_gainers.csv` or
  `evaluation-dataset.csv` as scanner templates** — those rows may include
  bars observed after the original scan. The feature row must always come
  from the saved scanner snapshot (`candidates.csv`); evaluation supplies
  only the outcome label and amplitude.
- Join evaluation labels back to `candidates.csv` by ticker, preset scan
  code, and exact scan time.

If strict promotion leaves both final families empty, return an empty
result — do not disable the low-amplitude template veto to manufacture a
candidate; that re-admits rows the evaluation feedback already identified as
weak and damages top-1 quality.

### Ranking (current state — see `SCANNER_MODEL.md` for validation history)

`ReRankCandidates` in `CandidateFinder.cs` ranks each family's **full**
candidate pool (admitted and `DiagnosticRejected` together — see
`SETTINGS_MAP.md` `EmitAllSeenCandidates`) by a **template-free quality
score**, validated by AUC against `evaluation-dataset.csv` before being wired
in:

- `CalculateRunawayLaunchQualityScore` (Runaway): Daily mid/upper-band tail
  slope + H4 mid-band tail slope + H4 upper-band tail slope (added
  2026-09-01, AUC 0.673 alone) + H4 band-width expansion (added 2026-09-01,
  AUC 0.639 alone) — combined AUC 0.62 → 0.69 on 422 decided rows, stable
  across a chronological split. See `SCANNER_MODEL.md` for the full feature
  sweep and rejected candidates (Weekly slopes, RSI, MACD histogram all
  tested weaker).
- `CalculateReversalHookQualityScore` (Reversal): Daily band-width
  compression + Daily lower-band hook tail slope.
- Sort key: `AdjustedRank = qualityScore * 50 + legacy NextDayRank`. The
  series-template tier/distance machinery (`ResolveTemplateRankTier`,
  `Diagnostics.TemplateRankTier`, etc.) still runs and writes to
  `candidates.csv` for audit, but does not affect sort order — check
  `RankingQualityScore` to explain why one candidate outranks another, not
  the template tier.

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
- Ranking/tiering: `ReRankCandidates` / `ResolveTemplateRankTier` in `CandidateFinder.cs`
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
