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
