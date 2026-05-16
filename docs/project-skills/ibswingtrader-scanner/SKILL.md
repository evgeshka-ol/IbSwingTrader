---
name: ibswingtrader-scanner
description: Use when working on the scanner, candidate ranking, wishlist promotion, TodayResearchLike vs Reversal separation, or tuning scanner settings in IbSwingTrader. Covers how the project selects candidates, how to read the main series, where the scanner logic lives, and which settings and outputs matter most.
---

# IbSwingTrader Scanner

Use this skill when changing scanner behavior or analyzing why a ticker was or was not surfaced by `get-candidates`.

## Core intent

The scanner's job is to find future fat moves early.

- For scanner quality, the main oracle is `AmplitudePct`, not `Win/Loss/NoEntry`.
- `NoEntry` may be a `TradePlan` problem.
- Low amplitude is a scanner problem.

## Pipeline

1. `get-candidates`
2. inspect `Data/Tickers/candidates.json`
3. inspect `Data/Tickers/wishlist.json`
4. compare yesterday's scan with today's `research_top_gainers.csv`

## Main split

- `ReversalCandidates`: below-mid / pullback / return-to-mean style ideas.
- `TodayResearchLikeCandidates`: above-mid / runaway / continuation style ideas.

Do not mix the two mentally or in code. They are opposite regimes and need different ranking logic.

## Main code

- Scanner core: `IbSwingTrader/Application/Candidates/CandidateFinder.cs`
- Output writer: `IbSwingTrader/Infrastructure/Logging/CandidateResultWriter.cs`
- Settings: `IbSwingTrader/agentsettings.json`
- Command: `IbSwingTrader/App/Commands/GetCandidatesCommand.cs`

## Read these references

- `references/PROJECT_OVERVIEW.md`
- `references/SCANNER_MODEL.md`
- `references/SERIES_PLAYBOOK.md`
- `references/SETTINGS_MAP.md`

## Practical workflow

When scanner quality is weak:

1. Check whether the ticker was fully missed, only reached `WishList`, or was present but ranked too low.
2. Use the series in `candidates.json` and `research_top_gainers.csv`.
3. Prefer fixing:
   - recall
   - `WishList -> TodayResearchLike` promotion
   - ranking
4. Touch `TradePlan` only after the list is already good.
