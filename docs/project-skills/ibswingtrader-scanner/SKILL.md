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

- Minimal near-term goal: the #1 current `Runaway` row should
  consistently become a practical winning idea and capture more than 10%.
  Optimize top-1 quality before widening attention to the rest of the list.
- For scanner quality, the main oracle is `AmplitudePct`, not `Win/Loss/NoEntry`.
- `NoEntry` may be a `TradePlan` problem.
- Low amplitude is a scanner problem.
- Priority #1: names in today's summary `Runaway` should be in tomorrow's `research_top_gainers.csv`.
- Series shape is a primary scanner signal: compare candidate rows literally against winner rows from `research_top_gainers.csv` and high-amplitude rows from `evaluation-dataset.csv`.

## Pipeline

1. `get-candidates`
2. inspect `Data/Tickers/candidates.csv`
3. compare yesterday's scan with today's `research_top_gainers.csv`

The scanner no longer uses `wishlist.csv` as an intermediate candidate stage.
Historical candle accumulation belongs to the historical cache, not to a
second candidate queue.

## Main split

- `Reversal`: below-mid / pullback / return-to-mean style ideas.
- `Runaway`: above-mid / runaway / continuation style ideas.

Internal `Runaway` subtypes:

- `BellUp`: squeeze-to-launch continuation, the canonical direct form
- `Runaway`: strict kink/acceleration launch
- `LaunchContinuation`: real Bollinger launch with strong daily/H4 expansion
- `PullbackContinuation`: constructive continuation after a pullback

Current final `Runaway` admission is intentionally strict: the candidate must
confirm `BellUp` on real Bollinger rows in either `H4` or `Daily`. Other
above-mid continuation subtypes remain diagnostic only.

Do not mix the two mentally or in code. They are opposite regimes and need different ranking logic.

Hard classification rule:

- The latest completed daily Bollinger mid bar is the primary hard split.
- If the ticker was below the daily Bollinger mid on the last closed daily bar, it belongs to `Reversal`.
- If the ticker was at or above the daily Bollinger mid on the last closed daily bar, it belongs to `Runaway`.
- Weekly and H4 context only refine subtyping, promotion, and ranking inside the family.
- Do not retune this family split when trying to improve list quality; keep the category boundary fixed and work only on promotion, ranking, and trade-plan behavior after the split.

Both families should produce meaningful future amplitude:

- `Runaway` should be the strongest next-day research proxy.
- `Reversal` should still usually produce `AmplitudePct > 10%`.
- If `Reversal` amplitude is below 10%, treat that as scanner failure, not a trade-plan issue.

Current final `Reversal` promotion uses the working `ReversalHook` pattern
after the hard split. The split only decides that the ticker is below the daily
Bollinger mid; `ReversalHook` decides whether it is a trade-ready return setup.
The hook is necessary but not sufficient: the combined real-D1 and Weekly/H4
rows must also match a high-amplitude (`AmplitudePct >= 10%`) reversal template.
The hook is detected on real daily rows:

- lower Bollinger band broke down and then hooks upward
- daily mid is still weak but the downward move is decelerating or turning
- band width is compressing after the breakdown
- MACD histogram is still weak/negative but turns upward toward zero
- MACD line and signal are converging
- RSI is recovering from the recent low

Good `ReversalHook` examples include POET, ASM, SSRM, CDE, and SVM from the
2026-06-15 evaluation set. The ideal entry is usually the first or second daily
bar after the lower-band hook; later scans may still work but are less clean.

## Series-template direction

When improving scanner recall, promotion, or ranking, prefer a literal series
similarity signal before adding more derived heuristics.

- Compare rows point-by-point with small tolerance, not only by computed slopes.
- Normalize comparable series from their first point so shape matters more than absolute level.
- Use `research_top_gainers.csv` as the main `Runaway` template source.
- Use high-amplitude `evaluation-dataset.csv` rows as an additional template source.
- Give extra weight to higher-amplitude template matches when the geometry is otherwise similar.
- For `Reversal`, use only high-amplitude reversal rows from the evaluation dataset.
- A candidate close to historical winner templates should get promotion/ranking support.
- A candidate close to low-amplitude or failed templates can later be penalized, but do not add that before the positive winner-template signal is stable.
- If a scan has many candidates above `AmplitudePct >= 10%`, treat them as
  the playable pool and learn low-amplitude rejection from rows below 10%.
  The goal is to remove the low-amplitude third by similarity to today's
  low-amplitude report rows, not by broad one-size-fits-all thresholds.
- If you need to reject weak candidates before evaluation knows the true
  `AmplitudePct`, do it late and only through row-based envelope expansion
  proxies. Do not hard-cut the family split or Bell classification.

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

Canonical Bell pattern pair:

- `BellUp`: squeeze, launch, then late flattening/mean-reversion warning on the direct bullish form
- `BellDown`: mirrored squeeze, launch down, then late flattening/mean-reversion warning on the bearish form

`BellUp` belongs to the above-mid continuation side and should be promoted when
the rows show a squeeze-to-expansion launch. In code, that can be recognized
either by a broader phase comparison or by a short local turn where the upper
and mid Bollinger rows bend up together and band width starts opening again.
`BellDown` is the mirrored form used on the below-mid reversal side.

The scanner does not require all three timeframes to confirm Bell.
One clean timeframe is enough:

- `H4`: eligible for final `Runaway`, play it today
- `Daily`: eligible for final `Runaway`, play it for tomorrow
- `Weekly`: diagnostic / next-week context only; do not promote to final `Runaway`

The source timeframe changes urgency and trade-plan depth, not the family split.

When the same runway pattern appears, split it by phase using only the saved
pre-move rows:

- `ReadyNow`: H4 real Bollinger trigger confirms the launch. The upper band
  expands upward, mid is not falling, lower band is not simply being dragged
  upward, and H4 RSI/MACD do not contradict the trigger. These candidates can
  be promoted to `Runaway`.
- `NotReady`: Daily/weekly runway shape exists, but H4/Daily trigger is not
  trade-ready. Reject it from the current final list. Do not force it into
  `Reversal`.
- `Neutral`: the saved rows do not confirm a trade-ready runway phase. Do not
  let legacy live-mover/template/bypass branches promote it into
  `Runaway`.

This split is important for same-pattern candidates: UMAC/ONDS-like rows are
ready for immediate `Runaway` admission, while SHLS-like rows with only a
higher-frame setup are rejected until a future scan finds a trade-ready
H4/Daily pattern.

Broader series direction: move pattern logic toward the visual indicators the
user actually relies on: Bollinger, MACD, and RSI. The priority order for
scanner prediction, filters, promotion, and ranking is:

1. real Bollinger upper/mid/lower curves
2. real MACD line, signal line, and histogram
3. real RSI as a confidence/ambiguity correction

MA rows and older distance/width rows are legacy/context. Do not base new
prediction logic on them unless a concrete analysis proves they add value
beyond the real Bollinger/MACD/RSI rows.

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

1. Check whether the ticker was missed by the market presets, rejected by the family pattern, or present but ranked too low.
2. Use the series in `candidates.csv` and `research_top_gainers.csv`.
3. Prefer fixing:
   - recall
   - family pattern recognition after the hard daily split
   - ranking
4. Touch `TradePlan` only after the list is already good.
