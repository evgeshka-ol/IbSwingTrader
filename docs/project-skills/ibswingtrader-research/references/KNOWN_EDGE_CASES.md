# Known Edge Cases

## TDIC-type garbage pumps

These may show huge amplitude but are dangerous garbage names.

Project response:

- recent daily price floor filter
- reject names that were very cheap in the recent completed daily window

## POET regime transition

`POET` is a canonical example of:

- first `reversal`
- then `runaway expansion`
- then `runaway up`

The same ticker can change family over time.

## MRAM stale leader

`MRAM` showed the stale-runaway problem:

- previously strong
- still attractive by raw series
- but already too late in live flow

This case motivated freshness and staleness adjustments.

## Borderline price-floor names

Examples like `FRMI` can sit just below the recent close floor.

This is where filter safety can collide with recall.

Treat these as borderline cases rather than obvious garbage.
