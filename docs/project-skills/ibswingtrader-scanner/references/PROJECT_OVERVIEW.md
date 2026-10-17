# Project Overview

`IbSwingTrader` is a scanner and evaluation pipeline for finding swing-trade candidates from IB/TWS market scans and historical data.

## Main commands

- `get-candidates`
  - checks TWS/IB Gateway connectivity and API readiness before starting the
    finder, including wishlist loading/history preparation; unavailable TWS or
    readiness timeout aborts the command via the existing connection-error
    handler (exit code 1), without writing candidates or wishlist
  - retains the local non-trading-day skip before attempting a connection
  - builds scanner output
  - writes `Data/Tickers/candidates.csv`
  - writes/updates `Data/Tickers/wishlist.csv`
  - watches meaningful (>=5% below closed Daily mid) loser-code drops for
    ReversalHook on subsequent scans; a completed-bar hook retires the cycle
  - persists inactive cycle memory in Data/Tickers/wishlist.lifecycle.json
    without keeping those names in active broker-backed watch batches
- `evaluate-candidates`
  - evaluates candidate outcomes
  - writes directly to `Data/datasets/evaluation-dataset.csv`
- `build-research-dataset`
  - builds today's oracle of strong movers
  - writes `Data/datasets/research_top_gainers.csv`
- `build-dataset` (added 2026-08-10)
  - reads real broker trade fills (not scanner candidates) from the trades CSV
  - `ITradePositionMerger` merges fills for the same ticker/direction within a
    settings-driven time window into one `TradeRecord` position (a broker
    per-order share cap can split one real position into several fills);
    positions below `MinimumEntryQuantity` are dropped as probe/experiment
    orders, not real trading decisions
  - fetches surrounding H4 candles per merged position and builds
    `TradeDatasetRow` rows with feature series, for studying real executed
    trades rather than scanner-found candidates
  - writes the trade dataset CSV via `ITradeDatasetBuilder`
  - settings: `BuildDatasetSettings` (`FillMergeWindowMinutes`, `MinimumEntryQuantity`, `TickerAliases`, ...)
  - command: `IbSwingTrader/App/Commands/BuildDatasetCommand.cs`
- `clean-up`
  - removes stale files and legacy rows

## Main artifacts

For user-installed automatic runs on Windows, see `docs/guides/scanner-schedule.md`
and `tools/Register-ScannerSchedule.ps1`. Schedule times are local Windows
wall-clock values, not ET. Scheduling invokes the existing get-candidates
command, with no separate pattern-specific scan mode. The signed-in user's
TWS/Gateway session must already be available. Registration/runs are user-owned.

- `Data/Tickers/candidates.csv`
- `Data/Tickers/wishlist.csv`
- `Data/datasets/evaluation-dataset.csv`
- `Data/datasets/research_top_gainers.csv`
- `Data/logs/log-*.log`

## Core idea

The project aims for dependable playable winners, starting with rank 1 and
profit of at least 1.5%. Follow the canonical
[success criteria](../../../guides/success-criteria.md): entered-plan wins,
winning profit percentages, then saved confidence-versus-outcome agreement.
Amplitude and next-day research coverage provide additional diagnostic evidence.

Current output groups are BellUp, ReversalHook and Other. These are pattern
groups, not the old below/above-Daily-mid families. Other retains unconfirmed
and non-ready cases for analysis; a zero plan is not a played trade.

The feedback loop joins saved candidates to subsequent evaluation outcomes
by ticker and ScanTime, examines rank 1 and the entered cohort, then uses
research movers and source series to explain recognition/recall/ranking or
execution failures. The requested dependable-winner goal is not yet an
established guarantee.

## Main code zones

- scanner: `Application/Candidates/`
- evaluator: `Application/Evaluation/`
- evaluation dataset builder: `Application/Dataset/EvaluationDatasetBuilder.cs`
- research dataset: `App/Commands/BuildResearchDatasetCommand.cs`
- settings: `agentsettings.json`
