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

Evaluation exists to answer two different questions:

- did the scanner find fat future movement
- did the trade plan execute it well

Do not mix them.

## Main rule

For scanner quality, the key metric is `AmplitudePct`.

**Category caveat (2026-09-09):** playable `Runaway` now focuses on BellUp;
`Other` holds nonmatching setups. In the September 8 snapshots, a row can
still have `CandidateGroup=Runaway` in `candidates.csv` but
`CandidateSource=Other`, a zero trade plan, and `CandidateGroup=Other` in
evaluation. The September 9 code change aligns output groups on the next
write, including legacy `DiagnosticRejected` rows under `Other`, and preserves
the source when loading candidates for evaluation. Existing CSV files have
not been regenerated. Match by ticker/ScanTime and inspect source before identifying
the playable top-1. `NoEntry` with a zero plan on an `Other` row is not
evidence of a mistimed/deep entry price. Current output details are in
`../ibswingtrader-scanner/references/SETTINGS_MAP.md`.

- High amplitude + `NoEntry` means scanner may be right and `TradePlan` may be wrong.
- Low amplitude means the scanner likely surfaced a weak ticker.
- The count of `Win` rows measures `TradePlan` conversion, not scanner recall.
- Many `Loss`, `NoEntry`, or `Open` rows with strong amplitude point to `TradePlan` failure.
- `Reversal` should still clear meaningful amplitude, normally `> 10%`; below that is scanner failure.

## Current data contract and pattern parity (2026-09-16)

- Candidate and evaluation trade columns use the same short names: `ScanPrice`,
  `EntryPrice`, `ExitPrice`, `StopLoss`, `StopLimitPrice`,
  `PlannedProfitPct`, `PlannedLossPct`, and `ExitProfile`. Candidate technical
  series are kept at the right side of the CSV after scalar fields.
- `PatternVerdictReason` is the explicit pattern/timeframe field. Its values
  omit the redundant `Reason=` prefix, for example `BellUp confirmed on H4`.
- The live scanner now has an experimental `Triangle` diagnostic (post-spike
  small-body consolidation) and routes it to non-playable `Other`. The
  evaluator's realized-row verdict path still reports BellUp/ReversalHook
  classifications and has not yet been made fully Triangle-aware; do not use
  evaluator BellUp output alone to claim scanner/evaluator parity for Triangle
  rows.
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

1. inspect yesterday's `evaluation-dataset.csv`
2. split weak rows from strong rows by amplitude
3. decide whether to fix:
   - scanner ranking
   - scanner recall
   - or `TradePlan`
