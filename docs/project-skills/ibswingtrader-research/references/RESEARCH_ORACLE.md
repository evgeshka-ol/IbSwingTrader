# Research Oracle

Main artifact:

- `Data/datasets/research_top_gainers.csv`

## Correct benchmark

Do not compare today's scanner to today's winners.

Compare:

- yesterday's scanner
against
- today's research winners

This measures whether the scanner surfaced fat movement early enough.

## Priority #1 benchmark

The most important benchmark is:

- today's current top-ranked `RunawayCandidates` rows in `candidates.csv`
- should become tomorrow's `research_top_gainers.csv` names

This is the primary direction for scanner tuning.

`RunawayCandidates` is not just a live-watch section. It is the
project's best attempt to predict the next research dataset.

When a current top-ranked `RunawayCandidates` name does not show up in the next
research dataset with strong amplitude, treat that as scanner selection/ranking
feedback before touching `TradePlan`.

## Classification

### Caught in current top rows

The ticker was already in yesterday's current top-ranked candidate rows.

This is the desired outcome.

For `RunawayCandidates`, this is the highest-value success case.

### Current top row failed next-day research

The ticker was in yesterday's current top-ranked `RunawayCandidates` rows, but did
not become a strong mover in today's research dataset.

This is scanner selection/ranking failure unless the amplitude was still strong
but the research threshold or universe missed it.

### Seen but not promoted

The ticker existed in yesterday's `WishList` or full `candidates.csv`, but not in current top-ranked rows.

This is promotion or ranking failure.

### Fully missed

The ticker was absent from yesterday's flow.

This is recall/universe/filter failure.

## Series-first rule

When a research winner is missed, inspect:

- weekly mid-distance / width / MACD
- daily mid-distance / width / MACD
- H4 mid-distance / width / MACD

The question is not only “was it up”.

The question is:

- what regime was it in
- when that regime became visible
- whether scanner logic knows how to promote that regime
