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

The active direction for entry prediction is four series-driven profiles:

- `FastContinuationShallow`: winner-like rows with no deep-pullback signature should use near-current/shallow entry. If earlier rules made entry too deep, cap the discount.
- `ModeratePullback`: alive but cooling rows should wait for a moderate pullback, not the old deep default.
- `DeepPullback`: deep entry is valid only when Daily/H4 rows show a real below-mean/deep-pullback shape.
- `AvoidLateSpike`: overheated late spikes near highs should require a very deep entry or be skipped by practical non-fill.

Daily below mean is not automatically a deep-pullback entry. If the row shows a
fresh launch from below daily mean with strong H4 acceleration, treat it as
`FastContinuationShallow`; otherwise winners like `AKAN` can become high-amplitude
`NoEntry` rows.

High-amplitude `NoEntry` rows usually mean the first two profiles are too deep.
High-amplitude `Loss` rows usually mean `AvoidLateSpike` or exit placement is too loose.
High-amplitude `Win` rows with large `ExitMissPct` or low captured percent mean
the exit target is too conservative for that setup family. For explosive
min-first rows, tune `DefaultProfitPct`; `MaxProfitPct` only caps the target and
does not raise it by itself.

When using evaluation rows for scanner feedback, split the series templates by
amplitude. Rows with `AmplitudePct >= 10%` are positive templates. Rows with
`SameDayContinuation` and `AmplitudePct < 10%` are negative ranking templates:
they should lower summary priority for candidates whose rows look similar.

## Current project principle

The project should first produce:

- a good `ReversalCandidates` list
- a good `TodayResearchLikeCandidates` list

Only after that should entry/exit tuning become the main focus.

## Dangerous anti-pattern

Do not patch scanner ranking by overfitting `TradePlan`.

That only hides list-quality problems.
