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

- For scanner quality, the main oracle is `AmplitudePct`, not `Win/Loss/NoEntry`.
- `NoEntry` may be a `TradePlan` problem.
- Low amplitude is a scanner problem.
- Priority #1: names in today's summary `TodayResearchLikeCandidates` should be in tomorrow's `research_top_gainers.csv`.
- Series shape is a primary scanner signal: compare candidate rows literally against winner rows from `research_top_gainers.csv` and high-amplitude rows from `evaluation-dataset.csv`.

## Pipeline

1. `get-candidates`
2. inspect `Data/Tickers/candidates.csv`
3. inspect `Data/Tickers/wishlist.json`
4. compare yesterday's scan with today's `research_top_gainers.csv`

## Main split

- `ReversalCandidates`: below-mid / pullback / return-to-mean style ideas.
- `TodayResearchLikeCandidates`: above-mid / runaway / continuation style ideas.

Do not mix the two mentally or in code. They are opposite regimes and need different ranking logic.

Hard classification rule:

- Anything above its mean context can only be considered for `TodayResearchLikeCandidates`.
- `ReversalCandidates` must be below mean on both the daily and weekly contexts.

Both families should produce meaningful future amplitude:

- `TodayResearchLikeCandidates` should be the strongest next-day research proxy.
- `ReversalCandidates` should still usually produce `AmplitudePct > 10%`.
- If `ReversalCandidates` amplitude is below 10%, treat that as scanner failure, not a trade-plan issue.

## Series-template direction

When improving scanner recall, promotion, or ranking, prefer a literal series
similarity signal before adding more derived heuristics.

- Compare rows point-by-point with small tolerance, not only by computed slopes.
- Normalize comparable series from their first point so shape matters more than absolute level.
- Use `research_top_gainers.csv` as the main `TodayResearchLikeCandidates` template source.
- Use high-amplitude `evaluation-dataset.csv` rows as an additional template source.
- For `ReversalCandidates`, use only high-amplitude reversal rows from the evaluation dataset.
- A candidate close to historical winner templates should get promotion/ranking support.
- A candidate close to low-amplitude or failed templates can later be penalized, but do not add that before the positive winner-template signal is stable.

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
2. Use the series in `candidates.csv` and `research_top_gainers.csv`.
3. Prefer fixing:
   - recall
   - `WishList -> TodayResearchLike` promotion
   - ranking
4. Touch `TradePlan` only after the list is already good.
