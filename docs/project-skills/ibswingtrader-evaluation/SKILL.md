---
name: ibswingtrader-evaluation
description: Use when working on candidate evaluation, evaluation-dataset interpretation, scanner quality feedback, amplitude analysis, or deciding whether a problem belongs to scanner ranking or trade plan in IbSwingTrader.
---

# IbSwingTrader Evaluation

Use this skill when interpreting `evaluation-dataset.csv` or changing evaluation logic.

## Responsibility boundary

Codex only analyzes data/code, changes code, and creates or updates documentation from that analysis.

The user runs builds, the application, scanner/research/evaluation commands, and tests. Do not try to launch them unless the user explicitly asks to change this rule.

## Core intent

Evaluate the published candidate and its saved plan against the canonical
[success criteria](../../guides/success-criteria.md), confirmed 2026-10-17:

1. winners among entered plans;
2. their entry-relative profit percentages;
3. agreement of saved DiagnosticsEstimatedHitRatePct with actual outcomes.

Minimum: a dependable rank-1 winner, with profit of at least 1.5% sufficient.
This is a target, not an established guarantee. Keep recognition/ranking and
execution mechanics distinguishable for diagnosis. Amplitude is supporting
evidence and does not replace Win, profit or confidence calibration.

## Main rule

Since 2026-10-07 (StrategyVersion=7), evaluation ScanPrice is the saved
candidate TradePlan.LiveReferencePrice, identical to candidates.csv ScanPrice.
It is never overwritten with the first post-publication M5 open. Scan-relative
percentages use this same reference; entry-relative profit/loss and outcome
remain unchanged. Dataset rebuilds restore the reference where an exact
candidate snapshot exists and rebase dependent metrics. Older unmatched rows
retain legacy semantics. Dated investigations remain historical records.

Since 2026-10-06, preserve saved `ReversalHook recognized on ...`,
`ReversalHook confirmed on ...`, and `ReversalHook unconfirmed on D1/H4`
reasons, including phases and NotReady details. A recognition Match on Other
is not trade admission. Reversal episodes can remain recognized above mid;
do not reintroduce a latest-price-below-mid gate during evaluation.

For current metric definitions and denominators, use the success criteria above.

**Current category contract (since 2026-09-23):** candidate groups are
`BellUp`, `ReversalHook`, and `Other`. Historical rows may retain the former
`Runaway`/`Reversal` values. When comparing candidate and evaluation data,
join by ticker and `ScanTime`, then inspect the saved `CandidateGroup`,
`CandidateSource`, and `PatternVerdictReason`; do not infer current pattern
membership from a legacy family label. An `Other` row with a zero plan is a
diagnostic, not evidence of a mistimed or deep entry. See scanner
`SETTINGS_MAP.md` for details.

Reversal phase reasons carrying `DiagnosticOnly=True` (since 2026-10-02)
are preserved from the scanner through evaluation and dataset construction.
Their `Match` verdict means the recorded diagnostic label is preserved, not
that the setup was admitted, traded, or profitable. Both D1/H4 phases and
the snapshot price remain scan-time evidence; do not replace that price with
the first post-publication M5 open when interpreting target progress.

Participation means an entry under the saved plan, checked with EntryTime.
Report entered Win/Loss/Open and NoEntry separately. Open is unresolved,
not automatically a loss. A high-amplitude Loss remains a played loss;
a 1.5% Win is not invalidated by amplitude below 10%. A zero-plan Other row
is not a played trade. Confidence calibration must respect model/scope and
plan policy; WinGivenEntryAndResolution uses Win/(Win+Loss), with Open and
NoEntry reported alongside it. Historical amplitude estimates stay separate.

## Current data contract and pattern parity (2026-09-29)

Since 2026-10-14, when entry and target both touch the first entry M5, replay
only that five-minute window with M1 before assigning Win. Discard pre-entry
minutes; do not infer intraminute order from candle color. Incomplete minute
coverage cannot confirm that parent's target; continue with later M5 using
conservative post-entry bounds. See EVALUATION_RULES.md for replay and retry scope.

Since 2026-10-05, new scanner `DiagnosticsEstimatedHitRatePct` values use
the experimental model `bellup-win-ridge-2026-10-05-v1`, scope
`WinGivenEntryAndResolution`. Historical percentages remain amplitude
estimates. Compare by model/scope metadata; the conditional estimate is
not a probability of Win for all candidates within ten days. Open/NoEntry
were excluded from model fitting; later calibration must account for them.

- Candidate and evaluation trade columns use the same short names: `ScanPrice`,
  `EntryPrice`, `ExitPrice`, `StopLoss`, `StopLimitPrice`,
  `PlannedProfitPct`, `PlannedLossPct`, and `ExitProfile`. Candidate technical
  series are kept at the right side of the CSV after scalar fields.
- `PatternVerdictReason` is the explicit pattern/timeframe field. Its values
  omit the redundant `Reason=` prefix, for example `BellUp confirmed on H4`.
- The candidate groups are `BellUp`, `ReversalHook`, and `Other`. Pattern
  classification no longer uses the old Daily-mid family split as its output
  taxonomy. `Other` includes confirmed setups rejected by readiness gates.
- The live scanner now has an experimental `Triangle` diagnostic (post-spike
  small-body consolidation) and routes it to non-playable `Other`. The
  evaluator preserves a saved `Other`/Triangle diagnosis as a Triangle match
  when processing current candidate rows. Its generic realized-row pattern
  recheck remains centered on BellUp/ReversalHook; this is not a full
  time-series Triangle replay.
- Preserve the distinction between pattern discovery and outcome measurement:
  a Triangle/Other row can have a large later amplitude, but it was not a
  playable BellUp candidate at scan time. Use the saved candidate snapshot and
  source fields when comparing such rows.

## Main code

- evaluator: `IbSwingTrader/Application/Evaluation/CandidateEvaluator.cs`
- pattern verdicts (Bell/ReversalHook re-check on realized rows): `IbSwingTrader/Application/Evaluation/CandidatePatternVerdictService.cs`
- evaluation dataset: `IbSwingTrader/Application/Dataset/EvaluationDatasetBuilder.cs`
- command: `IbSwingTrader/App/Commands/EvaluateCandidatesCommand.cs`

`CandidatePatternVerdictService.cs` classifies Bell/ReversalHook by calling the
same `BellPatternClassifier` (`Application/Candidates/BellPatternClassifier.cs`)
that the live scanner (`CandidateFinder.cs`) uses (as of 2026-08-10). Before
this extraction the two paths had quietly drifted apart, so a pattern verdict
here and the scanner's original decision could disagree even on the same rows.
If a pattern rule looks wrong here, fix it in `BellPatternClassifier.cs`, not
locally in this file — a local fix would immediately re-diverge from the live
scan path. See the `ibswingtrader-scanner` skill for the scanner side.

## Read these references

- `references/EVALUATION_RULES.md`
- `references/TRADEPLAN_SCOPE.md`

## Practical workflow

1. Inspect the original first playable row per scan and its evaluated result.
2. Join candidates and evaluations by ticker and ScanTime; report entered
   Win/Loss/Open, winning profit percentages and saved confidence calibration.
3. Use source series, price freshness, amplitude and research coverage to
   locate recall, recognition, ranking or execution problems.
4. Validate changes against the current success criteria, retaining separate
   observation horizons and repeated ticker/date dependencies.
