# TradePlan Scope

`TradePlan` is downstream of the list.

## Fix the scanner first when:

- many top candidates have low amplitude
- research winners are seen but not promoted
- summary contains stale or weak names
- `TodayResearchLikeCandidates` summary names do not become next-day research winners
- `ReversalCandidates` repeatedly produce amplitude below 10%

## Fix TradePlan first when:

- amplitude is strong
- scanner already found the right ticker
- but entry/exit quality is poor
- many strong rows end up `NoEntry`
- many strong-amplitude rows are `Loss` or remain `Open`

Large counts of `Loss` on strong-amplitude `TodayResearchLikeCandidates` usually
mean the entry was too early for the local structure. Do not treat this as a
simple "lower the entry" problem. The trade plan needs to predict entry and exit
from the same kind of series evidence used by the scanner.

## Metric split

Do not collapse scanner quality and trade-plan quality into one number.

- `AmplitudePct` measures scanner quality.
- Count of `Win` rows measures trade-plan conversion.
- Count of `Loss`, `NoEntry`, and `Open` rows with strong amplitude exposes trade-plan failure.

## Series-driven TradePlan Direction

The trade plan should use available rows/series from both:

- `evaluation-dataset.csv`
- `research_top_gainers.csv`

The goal is to learn/predict:

- whether entry should wait for a pullback or confirmation
- how deep the expected pullback is before the move continues
- whether the setup is min-first, max-first, same-bar, or stale
- where exit should be placed for that setup family

Useful fields include:

- `RecentDaily*Series`
- `RecentH4*Series`
- `RecentWeekly*Series`
- `ExtremumOrder`
- `EntryDistanceToMinAfterScanPct`
- `MaxPctBeforeEntry`
- `MinPctBeforeEntry`
- `PostMaxDrawdownPct`
- `MinutesFromMinToMax`
- `MinutesFromEntryToMax`
- `MinutesFromEntryToMin`

Do not patch this by only increasing stop size or applying a flat entry discount.
That hides the need for a predictive entry/exit model.

## Current project principle

The project should first produce:

- a good `ReversalCandidates` list
- a good `TodayResearchLikeCandidates` list

Only after that should entry/exit tuning become the main focus.

## Dangerous anti-pattern

Do not patch scanner ranking by overfitting `TradePlan`.

That only hides list-quality problems.
