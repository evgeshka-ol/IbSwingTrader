# Settings Map

Main settings file:

- `IbSwingTrader/agentsettings.json`

## High-impact scanner settings

### `GetCandidates.PreFilter`

Use for universe hygiene and garbage filtering.

Important examples:

- recent daily price floor
- low-price / penny rejection

Be careful: these filters can improve safety but also reduce recall.

### `GetCandidates.WishListFilter`

Controls who can enter `WishList`.

Use this to manage early scan breadth.

### `GetCandidates.CandidateFilter`

Controls conversion from `WishList` to candidate.

Useful when too many names are seen but not promoted.

### `GetCandidates.Finder`

- `EmitAllSeenCandidates`: temporary recall/ranking diagnostic mode. When
  `true`, the scanner writes seen-but-rejected tickers into the normal
  `candidates.csv` sections so the evaluator can score the full scanner input.
  Use this to judge whether high-amplitude names rank above weak names before
  re-enabling stricter final filters.

### `GetCandidates.NextDayRanking`

Controls final ordering.

Use this when the right names are present but top summary is wrong.

Since 2026-07-13, final order is driven by a template-free quality score
(`CalculateRunawayLaunchQualityScore` / `CalculateReversalHookQualityScore` in
`CandidateFinder.cs`, weighted by the hardcoded `QualityScoreRankWeight = 50`
constant, not a settings knob), not by `SeriesSimilarity` below. See
`references/SCANNER_MODEL.md` "Shared classifier implementation status" /
`SKILL.md` "Current ranking implementation" for what actually decides order
today.

### `GetCandidates.NextDayRanking.SeriesSimilarity` (diagnostic-only since 2026-07-13)

Controls literal row-shape matching against winner templates.

**These settings no longer decide final rank order.** They were the ranking
driver through 2026-07-11 (tiered sort by `Confirmed`/`Weak`/`None` +
template `AmplitudePct`), but that approach was empirically disproven on
2026-07-13 (every candidate came back `None` on a real scan despite templates
loading correctly — no discriminating signal in literal series distance for
this dataset). The tier/distance machinery below still runs and still writes
its result to `candidates.csv` for audit, but ranking now comes from the
quality score above. Do not tune `FullMatchDistance`/`WeakMatchDistance` to
try to fix a ranking problem — check `RankingQualityScore` instead.

Still-relevant knobs for interpreting the diagnostic columns:

- `MinTemplateAmplitudePct`: minimum amplitude for a row to become a positive template.
- `FullMatchDistance`: below this distance a match is the `Confirmed` rank tier.
- `WeakMatchDistance`: below this distance (and above `FullMatchDistance`) a
  match is the `Weak` rank tier; above it, there is no match at all (`None`).
- `FullMatchBonus` / `WeakMatchBonus`: only feed the diagnostic
  `SeriesSimilarityBonus` value written to `candidates.csv`.
- `EnableLowAmplitudePenalty`: enables negative templates from historical
  `Runaway` scan snapshots whose evaluation did not reach 10% amplitude. When
  `false`, no negative templates load and the low-amplitude veto never fires.
- `LowAmplitudeMinTemplateAmplitudePct` / `LowAmplitudeMaxTemplateAmplitudePct`:
  amplitude band for negative templates.
- `LowAmplitudePenaltyWeight`: only feeds the diagnostic `LowAmplitudePenalty`
  value in `candidates.csv`.
- `RelativePointTolerance` and `*PointTolerance`: per-point tolerance before a
  row difference is counted as real distance. Small differences such as `3.8`
  vs `4.0` should usually be treated as the same shape; larger differences
  should quickly reduce similarity.

Timeframes and indicator rows are not averaged. Daily and H4 are matched
independently, and each timeframe uses its worst real-line distance so one good
line cannot hide a failed Bollinger/MACD/RSI row. Weekly does not participate
in admission.

`FinalTopCandidates`, `SecondPassWindowMultiplier`, and
`SecondPassMinimumWindow` were removed on 2026-07-11: `Reversal` reranking
previously capped its second-pass window using these, silently leaving part of
a large list unadjusted by template matching. Both `Runaway` and `Reversal`
now always rerank their full candidate pool.

### `GetCandidates.TradePlan`

Do not use this to solve scanner recall/ranking issues.
Use it only after the list quality is good.

## Research settings

### `Research.RecentScanDays`

Use rolling day windows for the research output retention cutoff.

`0` or a missing/negative value keeps all existing research rows and appends
new daily oracle rows after de-duplication. `1` means today's research rows
only and will drop older rows from `research_top_gainers.csv`.

### `Research.RecentEvaluationScanDays`

Limits ticker universe taken from `evaluation-dataset.csv`.

### `Research.MaxParallelTickers`

Performance lever for research generation.

## Evaluation dataset settings

### `BuildEvaluationDataset.RecentScanDays`

Limits active rebuild window.

### `BuildEvaluationDataset.BackfillLegacyNoEntryZeroAmplitudeDays`

Temporary repair switch for legacy `NoEntry` rows with zero amplitude.
Turn it off after backfill is done.
