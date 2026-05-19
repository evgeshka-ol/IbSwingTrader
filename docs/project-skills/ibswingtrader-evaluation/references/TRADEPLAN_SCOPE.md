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

## Metric split

Do not collapse scanner quality and trade-plan quality into one number.

- `AmplitudePct` measures scanner quality.
- Count of `Win` rows measures trade-plan conversion.
- Count of `Loss`, `NoEntry`, and `Open` rows with strong amplitude exposes trade-plan failure.

## Current project principle

The project should first produce:

- a good `ReversalCandidates` list
- a good `TodayResearchLikeCandidates` list

Only after that should entry/exit tuning become the main focus.

## Dangerous anti-pattern

Do not patch scanner ranking by overfitting `TradePlan`.

That only hides list-quality problems.
