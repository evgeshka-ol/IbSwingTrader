# ReversalHook origin subcategories

Origin output since2026-10-22, following the user's GRAL chart review.
Main candidate groups remain BellUp/ReversalHook/Other. Origin describes
the setup independently of frame/phase. Since2026-10-23 PostBellUpRecovery
also determines its earlier Daily-mid completion boundary, below.

## User intent

PostBellUpRecovery describes a recent below-mid pullback and recovery after
an exhausted prior BellUp. DeepDeclineRecovery describes recovery after a
meaningful decline and prolonged below-mid context. An H4 signal in either
origin is not a confirmed Daily signal. A developing recovery may remain
sideways or become a Triangle; origin does not predict its eventual success.

The user selected ten closed Daily bars as an analysis window, not a universal
minimum duration. Prior BellUp identification requires history before that
window. The first implementation uses the shared Daily BellUp classifier;
short duration alone never establishes PostBellUpRecovery.

## Measurement and initial policy

Use completed **native D1**, full available indicator prefix and the scanner's
EMA20 Bollinger model. The latest below-mid close within the last10 bars
anchors a contiguous below-mid-close run, either ongoing or recently reclaimed.
Walk backward beyond the window to retain the true run duration when available.
Equality is not below. Depth is the greatest close discount to its own
contemporaneous EMA mid within that run; it is not future drawdown or amplitude.

1. DeepDeclineRecovery: run start is at/before the beginning of the10-bar
   context and maximum depth>=5%. A latest reclaim does not erase this origin.
   This is a descriptive window-based rule, not a10-day trade-entry requirement.
2. PostBellUpRecovery: run starts inside the context and a preceding Daily
   BellUp is found in up to30 completed bars **before the run begins**.
   At each historical point, use at most30 valid indicator rows and the shared
   BollingerFigureAnalyzer direction/BellPatternClassifier Daily definition.
   No subsequent or forming bar may establish that prior pattern.
3. Otherwise Unclassified, including missing/invalid native history, no recent
   below-mid episode, or a short dip without an identified preceding BellUp.

The meaningful-depth5% value reuses the user's wishlist depth convention;
it does not by itself admit a trade or change wishlist enrollment. Deep takes precedence
over a much older BellUp when a prolonged meaningful decline has intervened.
These are initial transparent semantic rules, not profit-validated thresholds.

Since2026-10-28, DeepDeclineRecovery also limits the separate
[current-week MACD support exception](REVERSAL_D1_WEEKLY_SUPPORT.md) for
active D1 hooks during W1 Down. PostBellUpRecovery and Unclassified do not
use that exception. Ordinary recognition, completion and fresh-plan checks remain.
Since2026-10-29 DeepDeclineRecovery also limits the separate
[gentle-W1 active/anticipatory D1 admission](WEEKLY_MID_SLOPE.md). It does not
give PostBellUpRecovery or Unclassified the new support path.

## Post-BellUp completion at Daily mid (2026-10-23)

User clarification after DHT, October22 17:19:12: a post-BellUp pullback's
return to Daily mid completes that hook for a new ReversalHook entry.
Do not wait for Daily upper or rely on further MACD room. DeepDeclineRecovery
retains its existing possibility of continuing past mid toward upper; this
is not a guarantee that it will do so. Unclassified is not silently assigned
the post-BellUp policy.

At publication, after computing origin from the same completed native D1
prefix, PostBellUpRecovery becomes admission Phase=Completed if:

- A completed Daily bar **strictly after RunStart** reaches its own EMA mid
  with its high (or close if high is missing). Equality counts, with full
  precision. Exclude the initial downward-cross candle: it passed through
  mid on the way down and cannot prove a subsequent return.
- Or the accepted fresh publication price reaches the last completed D1 mid.

A historical touch remains consumed after a pullback or later band movement.
Search is tied to the latest below-mid run; an independent new pullback has
a new boundary, rather than a permanent ticker blacklist. Never substitute
a future/forming Daily bar or chart SMA for native completed EMA evidence.

Reject before higher-frame support and trade-plan construction, keeping the
origin prefix, saved local diagnosis and explicit CompletionRule=
PostBellUpReturnToDailyMid/PostBellUpMidCompleted evidence in Other. No new
report columns or outcome backfill are introduced. The log includes
Reversal post-BellUp completed with the touch evidence.

For DHT, October21 Close24.16 exceeds its EMA mid23.59642; October22
ScanPrice24.52 therefore cannot be treated as an unfinished return-to-mid
entry. Earlier wick touches also count under the scanner's EMA model:
October16 High24.52 versus mid23.61224 already meets the touch rule after
RunStart October15. A body-close-only proof first arrives October21. This
does not force the earlier Yahoo/SMA chart interpretation into EMA dates.

## Evidence and model distinction

The diagnostic tail of PatternVerdictReason records context length, full observed below-mid duration,
maximum depth, run dates, ongoing/all-context-below/left-censored flags,
prior BellUp date and search bound, mid slopes before/since the cross and
BollingerModel. LeftCensored means the run reaches the first valid EMA point;
do not claim a known initial crossing date in that case.

The user's GRAL Yahoo chart uses SMA20: October16 mid131.52 matches the
cached SMA exactly. Scanner EMA on that date is126.18. At October21 the
respective mids are134.25 and126.37. The chart therefore has four below-SMA
closes and a wick-only reclaim; the scanner has three below-EMA closes and
a completed-body reclaim. Original OHLC corrections/chart feed differences
may exist too; do not force chart-bar count parity by changing scanner history.

The user highlighted a steep previously rising mid, its flattening at the
cross, and the outer bands' turns while the larger upward structure survives.
EMA can temporarily turn down during that same post-BellUp episode. Accordingly
mid slopes are **descriptive evidence**, not a hard positive-slope veto. No
switch back to SMA, new confidence weight or admission relaxation is implied.

## Persistence and compatibility

At the user's request, origin adds **no report columns**. PatternVerdictReason
starts with the subtype, for example:

```text
PostBellUpRecovery; ReversalHook confirmed on D1; ...
DeepDeclineRecovery; ReversalHook recognized on H4; Phase=Stalled; ...
```

Unclassified is also an explicit prefix when analysis cannot assign an origin.
BellUp reasons receive no reversal prefix. Recognized but rejected candidates
can retain the prefix in Other. The existing diagnostic evidence remains in
the same reason's tail. There are no ReversalOrigin/ReversalOriginReason DTO
properties or CSV columns.

Evaluation, saved-phase detection and ordering ignore the known subtype prefix
when interpreting the verdict, while preserving the complete original text.
Both old unprefixed and new prefixed reasons remain readable. Formatting a
reason repeatedly does not duplicate its prefix.

Compute at publication refresh from then-completed native Daily history.
Do not classify from H4-aggregated Daily fallback if native D1 is absent.
Historical unprefixed reasons remain unprefixed; opening/normalizing an old
CSV or refreshing feedback must not classify it with today's candles.
Evaluation preserves the original candidate's prefix through the saved reason;
rebuilds do not replace it with a later pattern or another origin.

Prepared user-run checks:

```powershell
dotnet run --project tests/ReversalOriginChecks
dotnet run --project tests/CandidateCsvChecks
dotnet run --project tests/EvaluationConfidenceChecks
```

Frozen October21 native D1 fixtures cover the user's eight examples, with
short-dip/no-prior-BellUp, prolonged tiny deviation, deep decline/reclaim,
missing history, duplicate chronology, source immutability and model evidence
controls. CSV/feedback checks cover persistence without changing group or
using future cache. Codex has not run these builds/tests or the application.
