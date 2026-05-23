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

### `GetCandidates.NextDayRanking`

Controls final ordering.

Use this when the right names are present but top summary is wrong.

### `GetCandidates.NextDayRanking.SeriesSimilarity`

Controls literal row-shape matching against winner templates.

Use this when candidates are present but the ranking misses names whose Daily,
Weekly, and H4 rows look like past `research_top_gainers.csv` winners or
high-amplitude evaluation rows.

Important knobs:

- `MinTemplateAmplitudePct`: minimum amplitude for a row to become a positive template.
- `FullMatchDistance` / `WeakMatchDistance`: row-similarity thresholds.
- `FullMatchBonus` / `WeakMatchBonus`: second-pass rank boost for close matches.
- `EnableLowAmplitudePenalty`: enables negative templates from
  `SameDayContinuation` evaluation rows that did not reach 10% amplitude.
- `LowAmplitudeMinTemplateAmplitudePct` / `LowAmplitudeMaxTemplateAmplitudePct`:
  amplitude band for negative templates.
- `LowAmplitudePenaltyWeight`: second-pass rank penalty for candidates matching
  low-amplitude row shapes.
- `DailyWeight`, `WeeklyWeight`, `H4Weight`: timeframe balance.
- `RelativePointTolerance` and `*PointTolerance`: per-point tolerance before a
  row difference is counted as real distance. Small differences such as `3.8`
  vs `4.0` should usually be treated as the same shape; larger differences
  should quickly reduce similarity.

### `GetCandidates.TradePlan`

Do not use this to solve scanner recall/ranking issues.
Use it only after the list quality is good.

## Research settings

### `Research.RecentScanDays`

Use rolling day windows instead of editing a manual date.

`1` means today's research rows only.

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
