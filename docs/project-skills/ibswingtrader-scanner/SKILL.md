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

- Minimal near-term goal: the #1 current `TodayResearchLikeCandidates` row should
  consistently become a practical winning idea and capture more than 10%.
  Optimize top-1 quality before widening attention to the rest of the list.
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
- If a scan has many candidates above `AmplitudePct >= 10%`, treat them as
  the playable pool and learn low-amplitude rejection from rows below 10%.
  The goal is to remove the low-amplitude third by similarity to today's
  low-amplitude report rows, not by broad one-size-fits-all thresholds.

## Bollinger pattern direction

Bollinger band shape patterns are timeframe-scalable. Do not treat them as
daily-only signals.

- `Weekly` Bollinger launch: broad background and rare large swing potential.
- `Daily` Bollinger launch: main swing/next-day potential, as in TE-style moves.
- `H4` Bollinger launch: early trigger, intraday capture, or next-day
  continuation, as in ONDS-style moves.

The same upper/mid/lower band pattern can be useful on any available timeframe,
but the trade decision changes with timeframe. Use the real band series
(`*BbUpperBandSeries`, `*BbMidBandSeries`, `*BbLowerBandSeries`) to detect the
shape, then use timeframe context to decide ranking strength and trade profile.

Broader series direction: move pattern logic toward the visual indicators the
user actually relies on: Bollinger, MACD, and RSI. MA is not a priority signal
for pattern recognition and should be treated as legacy/context unless a
specific analysis proves it adds value. Existing distance/width rows can remain
for compatibility and confirmation, but they should not be the only basis for
visual pattern recognition.

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
