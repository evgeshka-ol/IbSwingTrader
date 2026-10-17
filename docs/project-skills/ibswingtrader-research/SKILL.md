---
name: ibswingtrader-research
description: Use when working on the research oracle, comparing yesterday's scanner output to today's strongest movers, analyzing series in research_top_gainers.csv, or tuning scanner recall against real winners in IbSwingTrader.
---

# IbSwingTrader Research

Use this skill when comparing scanner output to the daily oracle of strong movers.

## Responsibility boundary

Codex only analyzes data/code, changes code, and creates or updates documentation from that analysis.

The user runs builds, the application, scanner/research/evaluation commands, and tests. Do not try to launch them unless the user explicitly asks to change this rule.

## Core intent and priority

Follow the canonical [success criteria](../../guides/success-criteria.md),
confirmed 2026-10-17: winners among entered plans, their profit percentages,
and confidence-versus-outcome agreement. The minimum goal is a dependable
rank-1 winner; 1.5% profit is sufficient. This is not an achieved guarantee.

research_top_gainers.csv supplies supporting evidence about strong moves
and recall. Compare yesterday's scanner with today's movers, not today's
scanner with today's winners. Coverage does not establish Win, execution
profit or confidence calibration; absence from the strong-mover dataset does
not invalidate a modest profitable trade.

Current groups are BellUp, ReversalHook and Other. Triangle is diagnostic
Other. Use saved group, source and PatternVerdictReason when distinguishing
missed playable opportunities from rejected or unconfirmed diagnostics.
NoEntry and zero-plan Other rows did not participate in a trade merely
because later movement was large.

## Main command

- `build-research-dataset`

## Main code

- `IbSwingTrader/App/Commands/BuildResearchDatasetCommand.cs`
- settings in `IbSwingTrader/agentsettings.json`

## Read these references

For the user's Triangle-completion / return-to-mid hypothesis, also read
`../ibswingtrader-scanner/references/TRIANGLE_RETURN_TO_MID.md`. It defines
the pending checks and the optional downside-excursion feature; current
Triangle output remains diagnostic Other.

- `references/RESEARCH_ORACLE.md`
- `references/KNOWN_EDGE_CASES.md`
- `references/WORKFLOW_CHECKLIST.md`

## Practical workflow

For the upper-band-versus-prior-candle hypothesis during post-rise pauses,
see `docs/investigations/2026-10/investigation-2026-10-15-pause-upper-position.md`. Its cache-only
study found no useful primary H4 separation; it is not a trade-outcome or
production-classifier replay. Upper below Low must not be treated as proof
of an impending П/Л fall or inability to continue a Triangle-like pause.

1. Inspect rank-1 outcome, entered Win/Loss/Open, winning profits and saved
   confidence calibration using candidate/evaluation snapshots.
2. Build today's research dataset and compare it with yesterday's scans
   as an additional recall diagnostic.
3. Classify movers as caught in playable summary, seen but not promoted,
   or fully missed. Keep realized trading results separate from mover labels.
4. Investigate recall, promotion or ranking gaps without replacing the
   current success criteria with amplitude or next-day coverage.
