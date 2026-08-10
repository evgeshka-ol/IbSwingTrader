# Project Overview

`IbSwingTrader` is a scanner and evaluation pipeline for finding swing-trade candidates from IB/TWS market scans and historical data.

## Main commands

- `get-candidates`
  - builds scanner output
  - writes `Data/Tickers/candidates.csv`
  - writes/updates `Data/Tickers/wishlist.csv`
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

- `Data/Tickers/candidates.csv`
- `Data/Tickers/wishlist.csv`
- `Data/datasets/evaluation-dataset.csv`
- `Data/datasets/research_top_gainers.csv`
- `Data/logs/log-*.log`

## Core idea

The project tries to identify fat moves before the main expansion, then separate:

- reversal / return setups
- above-mid continuation / runaway setups

The most important feedback loop is:

1. yesterday's scanner output
2. today's `research_top_gainers.csv`
3. today's `evaluation-dataset.csv`

This tells you:

- what the scanner caught early
- what it saw but ranked too low
- what it missed completely

## Main code zones

- scanner: `Application/Candidates/`
- evaluator: `Application/Evaluation/`
- evaluation dataset builder: `Application/Dataset/EvaluationDatasetBuilder.cs`
- research dataset: `App/Commands/BuildResearchDatasetCommand.cs`
- settings: `agentsettings.json`
