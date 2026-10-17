# Reversal watch lifecycle

Current contract, user clarification 2026-10-17. The wishlist's only purpose
is catching ReversalHook after a meaningful drop, not tracking any old loser
or generating BellUp opportunities. General scanner admission/ranking and the
[current success criteria](../../../guides/success-criteria.md) remain separate.

## Enrollment

A current TOP_OPEN_PERC_LOSE/TOP_PERC_LOSE sighting must pass the basic
instrument filter and have fresh M5 price at least 5% below the Bollinger
mid of the last completed native D1: price <= mid * 0.95. Exactly 5% qualifies.
The user's threshold applies equally to new additions and re-enrollment.
No tiny crossing, above-mid pullback, missing/stale price or provisional
Daily mid qualifies. SETTINGS_MAP.md describes price freshness/history rules.
Pattern readiness and entry checks follow enrollment; a fallen name need
not already be ready to trade to be watched. A current direct watch attempt
does not renew the 30-calendar-day loser-sighting retention window.

## Consume the cycle when a hook occurs

Use the existing shared ReversalHook classifier on completed native D1/H4
prefixes. It exposes the actual seed's age: the hook timestamp is the seed's
completion time (D1 16:00; H4 start+4h), not the later diagnosis time.
Either a middle-band seed or the existing historical strict hook route counts.
Recognition consumes the watch cycle even when the seed is now Stalled or
Completed, the current plan is Other, or price/support checks prevent entry.
A future win, amplitude or incomplete candle never establishes this event.

Remove consumed cycles from wishlist.csv. Cached history is inspected before
watch batches are created so an old hook does not trigger recurring broker
loads. Initial migration inspects available cached prefixes; later passes
only classify newly completed prefixes, using persisted per-frame cursors.
The bulk native indicator pass reuses FeatureEngine's Bollinger/RSI/MACD
primitives without recomputing unrelated cross-timeframe features per bar.
This is lifecycle bookkeeping, not new pattern thresholds or outcome validation.

A newly observed hook still recognized in the final six-bar window gets one
normal watch-source publication attempt in that run, despite CSV retirement.
It adds to a positive active-watch batch limit. Failed or interrupted attempts
are not a persistent trading queue; further live-code sightings remain subject
to the normal scanner. The hook alone never guarantees a published trade.

## New drop after a consumed hook

Remember the consumed cycle in wishlist.lifecycle.json. Repeated loser-code
sightings while still in the same >=5%-below-mid zone do not restart it.
A completed native D1 close or fresh current M5 observation outside that zone
after the hook arms a subsequent excursion back into it. Actual re-enrollment
still needs a current loser-code sighting and a fresh qualifying M5 price.
It does not require breaking the old hook's historical low. Starting a new
cycle preserves the consumed-hook memory; an old seed cannot consume it again.

Existing validated watches may recover toward or above mid while waiting
for recognition. The depth rule governs adding/renewing, not the pattern's
ongoing recognition after mid reclaim. A live scanner code may independently
surface the same ticker using the normal BellUp -> ReversalHook -> Other
pipeline. A watch-only context cannot publish BellUp. If a current live
context exists for that ticker, omit its duplicate watch-only context.

## Persistence and migration

wishlist.csv contains only active rows. wishlist.lifecycle.json stores
per-ticker last-hook confirmation, cycle start, rearm state and inspected
D1/H4 bar starts. The ledger is not a request universe and is not expired
alongside active rows, so deleting/expiring a CSV row cannot forget its hook.
Both files use temporary-file replacement; ledger is saved before CSV so
an interrupted CSV write cannot resurrect a consumed cycle at the next load.
They are not a two-file transaction. Keep the ledger when moving the wishlist.
Invalid ledger JSON or invalid depth settings abort rather than silently reset
cycle memory. Disabling ReversalWatchList leaves both files intact.

On the first updated run, discard non-loser legacy rows. Inspect available
cached history for already consumed cycles. Remaining legacy rows without a
validated cycle start need the fresh native-D1/M5 depth check: shallow ones
leave the CSV, qualifying ones gain a validated cycle without renewing their
LastDropSeenAt. Missing price/history retains the row but defers watch-only
candidate processing. No generated CSV/cache is manually rewritten by Codex.
Historical cache is retrospective and may be incomplete/corrected; lack of a
recognized cached seed does not prove that no manual hook ever occurred.
Unchanged/older prefixes are not continually reclassified after migration.

## Evidence and user-run checks

WEAV cache has a D1 mid-bend seed on 2026-08-18, with Close 7.28 following
5.53 on August 17; later prices remained around 7.3-7.4. Its saved October 16
enrollment price about 7.35 versus Daily mid 7.3635 was only about 0.18% below
mid. It passed the former strict-below rule but cannot pass the new 5% rule.
Cache-only arithmetic corroborates the date; no full historical C# publication
replay or timely broker availability is claimed.

Relevant source: ReversalWatchList.cs, ReversalWatchState.cs, CandidateFinder.cs,
BellPatternClassifier.TryFindReversalHookSignal and
FeatureEngine.CalculateNativeReversalHistory.

User-run commands:

```text
dotnet run --project tests/ReversalWatchListChecks
dotnet run --project tests/ScannerTimingChecks
```

The new checks cover depth boundaries, cycle consumption/rearm/persistence,
old-seed protection, forming-bar exclusion and exact indicator parity with the
existing per-prefix engine on both native scales. They are prepared, not run.
Observe Reversal watch retired, hook observed, removed legacy shallow drop,
enrollment check/QualifyingDrop and hook-already-observed-awaiting-new-drop
in the next scan. Actual migration size and performance require a user-run scan.
