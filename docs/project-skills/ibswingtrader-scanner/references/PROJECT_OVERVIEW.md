# Project Overview

`IbSwingTrader` is a scanner and evaluation pipeline for finding swing-trade candidates from IB/TWS market scans and historical data.

## Main commands

- `get-candidates`
  - builds scanner output
  - writes `Data/Tickers/candidates.json`
  - writes/updates `Data/Tickers/wishlist.json`
- `evaluate-candidates`
  - evaluates candidate outcomes
  - writes directly to `Data/datasets/evaluation-dataset.csv`
- `build-research-dataset`
  - builds today's oracle of strong movers
  - writes `Data/datasets/research_top_gainers.csv`
- `clean-up`
  - removes stale files and legacy rows

## Main artifacts

- `Data/Tickers/candidates.json`
- `Data/Tickers/wishlist.json`
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
