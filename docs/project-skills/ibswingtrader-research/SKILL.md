---
name: ibswingtrader-research
description: Use when working on the research oracle, comparing yesterday's scanner output to today's strongest movers, analyzing series in research_top_gainers.csv, or tuning scanner recall against real winners in IbSwingTrader.
---

# IbSwingTrader Research

Use this skill when comparing scanner output to the daily oracle of strong movers.

## Responsibility boundary

Codex only analyzes data/code, changes code, and creates or updates documentation from that analysis.

The user runs builds, the application, scanner/research/evaluation commands, and tests. Do not try to launch them unless the user explicitly asks to change this rule.

## Core intent

`research_top_gainers.csv` is the oracle for today's strongest moves.

Its main job is to answer:

- which names were actually fat movers today
- whether yesterday's scanner already saw and promoted them

## Current top priority

Priority #1 is next-day research coverage from `TodayResearchLikeCandidates`.

Tickers that appear in today's `candidates.json` summary section
`TodayResearchLikeCandidates` should appear in tomorrow's
`research_top_gainers.csv`.

When this does not happen, treat it as the primary scanner feedback loop:

- if the summary names do not reach strong amplitude, fix scanner selection/ranking
- if research winners were only in full data or wishlist, fix promotion/ranking
- if research winners were absent entirely, fix recall/universe/filtering

## Main command

- `build-research-dataset`

## Main code

- `IbSwingTrader/App/Commands/BuildResearchDatasetCommand.cs`
- settings in `IbSwingTrader/agentsettings.json`

## Read these references

- `references/RESEARCH_ORACLE.md`
- `references/KNOWN_EDGE_CASES.md`
- `references/WORKFLOW_CHECKLIST.md`

## Practical workflow

1. build today's research dataset
2. compare it with yesterday's `candidates.json`
3. classify:
   - caught in summary
   - summary name failed to become a research winner
   - seen but not promoted
   - fully missed
4. use series to decide whether the next fix belongs to:
   - recall
   - promotion
   - ranking
