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

## Classification

### Caught in summary

The ticker was already in yesterday's top summary.

This is the desired outcome.

### Seen but not promoted

The ticker existed in yesterday's `WishList` or full `candidates.json`, but not in summary.

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
