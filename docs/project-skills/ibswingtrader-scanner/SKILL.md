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

Follow the canonical [success criteria](../../guides/success-criteria.md)
(updated 2026-10-17): winners among entered plans, their profit percentages,
and agreement of saved DiagnosticsEstimatedHitRatePct with outcomes, in that
order. Minimum: a dependable winner at rank 1; profit of 1.5% is sufficient.
This is the user's target, not a demonstrated guarantee. Amplitude and
next-day research coverage are supporting diagnostics, not the main oracle.

## Current contract

Bollinger now uses EMA20 center with +/-2 population sigma over the last20
closes (SMA20 seed). New snapshots carry BollingerModel; old snapshots remain
legacy. Current H4 momentum may support a recognized mid-bend episode's live
mid reclaim rather than waiting for closed Daily MACD. Read the exact model,
freshness and migration rules in
[Bollinger EMA and recovery](references/BOLLINGER_EMA_AND_RECOVERY.md).

- Output groups are BellUp, ReversalHook and Other. The former Daily-mid
  Runaway/Reversal split is historical; it does not determine current groups.
- BellUp has precedence. A recognized BellUp rejected by timing/readiness
  remains Other, rather than being relabeled ReversalHook. Triangle is a
  diagnostic fallback and must not hide BellUp confirmed on another timeframe.
- ReversalHook recognizes recent completed episodes independently on D1/H4,
  even with sufficient D1 history and after price crosses mid. Recognition
  and readiness are distinct. Publication needs an Active/MidCrossed episode,
  higher-timeframe support and a fresh valid plan. Other has no active plan.
- A completed-bar upper-band touch consumes the current reversal impulse even
  after a pullback. A completed H4 seeded inside the current D1 episode blocks
  readmission through that D1 signal. See REVERSAL_IMPULSE_COMPLETION.md for
  exact boundaries, saved evidence and independent-signal exceptions.
- TWS-native completed bars are canonical. Never substitute a chart's
  session grid, final forming-bar values or future outcomes for scan-time data.
  Weekly supplies context; confirmed Bell patterns use H4/Daily.
- BellUp Recent boost timing uses the latest green completed candle's range
  against the median of five preceding ranges (default multiplier 2).
  Confirmed Daily timing can veto an H4 signal; absent Daily confirmation
  cannot. Exit boost selection remains body-based. Do not merge these rules.
- Final BellUp profit targets >=10% are divided by two by default, preserving
  the existing 1.5% floor. This is execution policy, not a success requirement
  to capture >10%. See SETTINGS_MAP.md for exact rounding and controls.
- DiagnosticsEstimatedHitRatePct uses experimental BellUp model
  bellup-win-ridge-2026-10-05-v1 after publication pricing; scope is
  WinGivenEntryAndResolution. Console uses Win~. Unsupported plans show n/a.
  This estimate is not the current ranking sort key or a guaranteed win.
- Wishlist exists only to catch ReversalHook after a meaningful drop. Loser
  enrollment/renewal requires fresh M5 at least 5% below the last completed
  native D1 mid. A recognized completed-bar hook consumes the cycle; a new
  deep-drop excursion is needed to re-enroll. Watch-only contexts cannot
  publish BellUp. See REVERSAL_WATCH_LIFECYCLE.md; current live sources retain
  their normal pipeline. This watch rule is not a global hook admission gate.
- Before-session/missing-price loser observations persist in wishlist.pending.json
  and retry at in-run checkpoints after the configured session opens. They remain
  outside active wishlist.csv until fresh-price/depth/lifecycle checks pass.
  See REVERSAL_WATCH_LIFECYCLE.md for expiry, bounded retries and persistence.
- Reversal M5 bridges are bounded; history/features are reused within a run.
  Final publication pricing remains fresh. Recognized reversals may use the
  configured IB live-quote fallback; delayed/frozen data cannot authorize entry.
- BellUpPauseDiagnostic and Reversal supplemental/partial-bar/lower-body
  diagnostics do not authorize entry or change plans, rank or Win~.
- Feature rows come from the saved scanner snapshot; evaluation supplies
  outcomes. Preserve price/time/source provenance and completion boundaries.

Signal priority: real Bollinger upper/mid/lower curves, then MACD
line/signal/histogram, then RSI. MA and distance/width rows are legacy context.
Validate candidate features empirically before retuning constants; removed
literal template matching must not be revived without new evidence.

## Ranking (current state — see `SCANNER_MODEL.md` for validation history)

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
  (removed 2026-09-01; see MODEL_HISTORY.md) — check `RankingQualityScore` to explain
  why one candidate outranks another.

Bell/ReversalHook classification (envelope math, curve-turn checks,
vertical-spike detection) lives in one shared place, `BellPatternClassifier`
(`Application/Candidates/BellPatternClassifier.cs`), used by both the live
scan (`CandidateFinder.cs`) and offline evaluation
(`CandidatePatternVerdictService.cs`). Change a rule there once, not in
either caller — the two paths silently drifted apart before this
consolidation.

Before adding or reweighting a ranking feature, validate the declared
Win/profit target and rank-1 results using saved scan-time features and later
outcomes. Historical amplitude AUC above does not validate present Win
performance. See [success criteria](../../guides/success-criteria.md).

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

## Read the matching references

- [Success criteria](../../guides/success-criteria.md): required metrics,
  rank-1 goal, participation and confidence denominators.
- [Project overview](references/PROJECT_OVERVIEW.md): commands, artifacts,
  TWS readiness and source-code zones.
- [Series Playbook](references/SERIES_PLAYBOOK.md): exact geometry, phases,
  cross-timeframe support, publication, timing and diagnostic boundaries.
- [Bollinger EMA and recovery](references/BOLLINGER_EMA_AND_RECOVERY.md): shared
  formula, metadata, current H4 MACD support and preserved publication checks.
- [Reversal watch lifecycle](references/REVERSAL_WATCH_LIFECYCLE.md): 5% Daily-mid
  enrollment, hook retirement, new-drop rearm, persistence and legacy migration.
- [Completed reversal impulses](references/REVERSAL_IMPULSE_COMPLETION.md):
  historical upper-band touches and same-episode H4/D1 admission guards.
- [Settings map](references/SETTINGS_MAP.md): watch retention/enrollment,
  data guards, pricing fallback, output fields and execution controls.
- [Scanner model](references/SCANNER_MODEL.md): failure modes, current score
  implementation and historical feature-validation results.
- [Reversal edge](references/REVERSAL_EDGE.md): the user's manual trigger,
  worked examples and historical/unvalidated research proposals.
- [Model history](references/MODEL_HISTORY.md): former family split,
  removed template machinery and unvalidated architecture proposals.
- [Triangle return to mid](references/TRIANGLE_RETURN_TO_MID.md): pending
  falling-Triangle hypothesis, distinct from the deployed post-rise diagnostic.

## Practical workflow

1. Inspect the original rank-1 playable row and its saved plan/confidence;
   join outcomes by ticker and ScanTime. Record group and ranking scope.
2. Report entered Win/Loss/Open, winning profit percentages and compatible
   confidence calibration. Keep NoEntry and incomplete horizons explicit.
3. Diagnose the cause: recall, recognition/readiness, ranking, price freshness
   or plan execution. Use amplitude/research coverage as supporting evidence.
4. Validate the proposed fix against the current success criteria on later
   data; do not retune a mechanism with no demonstrated predictive signal.
