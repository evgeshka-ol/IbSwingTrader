# Gentle W1 support and anticipatory Daily reversal admission

Date:2026-10-29. Implemented at the user's explicit request following the
[Other cohort study](../investigations/2026-10/investigation-2026-10-29-other-weekly-slope.md).
Current contract: [weekly-mid support](../project-skills/ibswingtrader-scanner/references/WEEKLY_MID_SLOPE.md).

## Resulting behavior

A recognized D1 DeepDeclineRecovery may use a gently declining weekly mid
as higher-frame support despite the old cumulative Down label. Full-precision
geometric change across the latest three available W1 observations must be
between−1.5% and0% per transition, with neither transition below−1.5%.
W1 Collapse, completed impulses and BellUp precedence remain vetoes.

Both paths need expected-date completed native D1 history, improving Daily
MACD histogram, at least2% room to W1 mid and a valid fresh quote/H4 base:

- Active/MidCrossed additionally needs improving closed and fresh H4 MACD.
- Stalled may use AnticipatoryD1Recovery without H4 MACD improvement or a
  price reclaim above the old pre-bend close. It needs at least2% room to
  D1 mid and a fresh price at/above the valid last completed Daily low.
  Its local Stalled phase is retained in the admitted reason, not relabeled.

For SOFI's October26 prefix the latter targets the actual failure: Daily
MACD improves, W1 is gentle and price14.43 remains above Daily low14.40,
while closed/fresh H4 momentum weakens. The policy is designed to allow
that context through support/phase admission; actual publication still needs
the fresh quote, BellUp precedence and valid rebuilt plan. No full application
replay or historical profitable admission is claimed.

New admitted plans use the existing direct ScanPrice entry. TradeBuilder
rebuilds target/stop, and publication verifies entry/reference equality and
valid levels. No proxy-study target/stop, ranking feature, probability model,
extra report column or broker request was added. Native history and existing
in-run feature caches supply the new calculation.

## Implementation

- WeeklyMidSlope measures geometry without changing the shared Up/Down/Flat
  enum or the old direction classifier. Its boundary comparison uses Decimal
  squared ratios rather than a rounded/double square-root classification.
- ReversalGentleWeeklySupport checks active and anticipatory paths. Typed
  ReversalRecoveryMomentum.IsAvailable is set only by successful existing
  session/base/quote validation; weak and unavailable are distinct.
- CandidateFinder evaluates this support after origin/completion checks and
  before the ordinary phase rejection. Full-precision weekly sampling uses
  completed native H4 prefixes; exported rounded weekly rows now refresh from
  the same prefix. Source/confirmation time, slope and admission evidence are
  saved in PatternVerdictReason and the existing log.
- ReversalImpulseCompletion also consumes Stalled D1 when a contained H4
  impulse has already completed. This prevents anticipatory readmission of
  an impulse consumed on the smaller scale.

Existing saved candidates, evaluation outcomes, confidence estimates and
dated studies remain unchanged. Evaluation already preserves confirmed
ReversalHook reasons, including the new explicit anticipatory evidence.

## Verification

ReversalRecoveryChecks adds exact slope-boundary, single-step-drop, missing/
future/stale data, non-deep/H4-only/unrecognized/completed, momentum, room,
Daily-low and contained-H4-completion controls. A frozen SOFI fixture contains
166 native D1 and599 completed H4 bars ending October26. Two H4 OHLC fields
are restored from the original log because the mutable cache later differs
by one cent; provenance is included. No October27 candle/outcome is present.
It uses the actual FeatureEngine and phase classifier, retains Stalled and
prepares an anticipatory-support acceptance check with valid weak H4.

ReversalEntryChecks additionally passes that frozen H4 history and14.43
reference through the real TradeBuilder's direct-entry path. This is plan
construction, not a complete CandidateFinder/broker replay.

Codex performed source/documentation edits and static diff/XML/fixture/link
review. On2026-10-29 the user reported successful completion of all four
commands below: application build, ReversalRecoveryChecks, ReversalEntryChecks
and ReversalWatchListChecks. This is user-reported verification; console output
and check counts were not supplied. Codex did not run these commands or make
broker requests.

```powershell
dotnet build IbSwingTrader/IbSwingTrader.csproj
dotnet run --project tests/ReversalRecoveryChecks
dotnet run --project tests/ReversalEntryChecks
dotnet run --project tests/ReversalWatchListChecks
```

These results confirm the user-run build and prepared regressions, including
the frozen SOFI support/plan checks. Live scanner publication and subsequent
trade outcomes under the new policy remain to be observed.

The exploratory evidence has missing coverage, dependent repeated tickers and
a selected favorable case date. The new narrower guards are not a fitted
Win model. Assess future entered Win/Loss/Open, profit, confidence compatibility
and original rank1 outcomes under this policy.
