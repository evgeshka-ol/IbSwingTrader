# Weekly mid: gentle decline versus steep decline

Current publication policy, user-authorized2026-10-29, following SOFI and the
October26 Other cohort. WeeklyMidSlope and ReversalGentleWeeklySupport add two
bounded D1 admission paths. The global Up/Down/Flat direction enum is unchanged.

## Intended meaning

A slightly declining, almost horizontal W1 mid above price can remain a useful
return-to-mean reference in the user's chart interpretation. Its negative sign
alone must not be described as evidence of a steep decline. This is a context
feature for the rules below, not a proven force or guaranteed return to mid.

Current BollingerFigureAnalyzer uses rounded total percentage change across
the entire supplied mid series: positive=Up, negative=Down, zero=Flat. It has
no separate gentle-decline state. A long negative history and a recently
flattening tail are therefore collapsed into the same Down label. Conversely,
THRM October26 retains an Up aggregate despite a falling recent tail.

## Measurement independent of chart zoom

A screen angle is not invariant: resizing axes or switching price/log scales
changes it. Use percentage change per W1 observation, with an explicit window.
For the last three positive weekly mid observations m0,m1,m2:

```text
RecentSlopePctPerWeek = 100 * (sqrt(m2 / m0) - 1)
Step1Pct = 100 * (m1 / m0 - 1)
Step2Pct = 100 * (m2 / m1 - 1)
WorstStepPct = min(Step1Pct, Step2Pct)
```

The publication rule's **gentle decline** is:

```text
-1.5 <= RecentSlopePctPerWeek <= 0
WorstStepPct >= -1.5
```

The second condition prevents a steep single-step drop being hidden by the
average. SOFI's16.78→16.57→16.37 gives−1.229% per transition and worst
step−1.251%, so it qualifies. ARLO gives−0.379%; THRM−1.661% does not qualify.
This user-authorized boundary describes the SOFI anchor; it was not optimized
for the best outcome count. It is not a universal mathematical distinction or
a calibrated safe-entry boundary. Sensitivity at0.5/1/1.5/2/3% remains in the study.
The1.5% slope bound is independent of the user's1.5% minimum profit objective.

Also retain the preceding two-transition slope, each individual step and the
total long-window change. Becoming less negative and already being shallow
are distinct facts; neither alone proves that price has begun recovering.

## Time and model provenance

The historical study uses the exact saved rounded WeeklyBbMidBandSeries.
BuildRecentWeeklySeries samples one available prefix per week, including the
then-observed unfinished current week. Do not substitute that week's later
final close. A Monday observation is provisional weekly context, not a fully
elapsed new week. Keep completed-week-only sensitivity distinct.

Publication calculates the weekly tail at full precision from the completed
native H4 prefix, with the same FeatureEngine/weekly sampling as existing
exports. It does not reseed EMA in the three-point window. New snapshots refresh
the rounded weekly exports from that same completed prefix. The still-forming
week uses only information confirmed at decision time, never its later close.
Reasons retain WeeklySource/WeeklyObservationConfirmedAt, full-precision slope,
each step, last completed D1, Daily MACD values and fresh H4 quote provenance.
Old H4-aggregated weekly exports and native-D1-derived weekly models remain
distinct. Old candidates/outcomes are not recomputed or relabeled.

## Two publication paths

Both require a recognized **D1** DeepDeclineRecovery, W1 not Collapse, gentle
weekly geometry, at least2% remaining room from the fresh price to W1 mid,
and strictly improving last **completed native D1** MACD histogram. Native D1
must have at least21 bars, positive chronological closes, no forming/future
bar and the latest date expected from the configured exchange calendar.
Fresh H4 evidence must pass the existing native-session, expected closed-base,
quote timestamp/receipt and age guards. Typed IsAvailable distinguishes weak
momentum from missing/stale evidence; diagnostic strings never authorize entry.

1. **ActiveD1Recovery:** local phase Active/MidCrossed, strictly improving
   completed H4 histogram and improving fresh provisional H4 histogram.
2. **AnticipatoryD1Recovery:** local phase Stalled is retained. At least2%
   remaining room to the native D1 mid is also required, with a valid last
   Daily range and fresh price at/above its low. Breaking that low rejects.
   Closed/fresh H4 histogram improvement and recovery above the pre-bend close
   are intentionally not required by this user-authorized anticipatory path.
   This is not a relabeling of Stalled as Active. Saved reasons explicitly
   include Admission=AnticipatoryD1Recovery and LocalPhase=Stalled.

Unlike the October28 current-week-MACD exception, these paths do not need two
completed D1 observations in the current week and may operate on Monday.
The old support paths remain available when this geometry does not qualify.
W1 Collapse, unrecognized/H4-only origins, PostBellUpRecovery/Unclassified,
Completed/Preparing/invalidated phases and missing fresh evidence cannot use
the new paths. BellUp precedence, local upper touches and post-BellUp Daily-mid
completion remain ahead of admission. Same-episode completed H4 also consumes
a Stalled D1 signal, closing the potential anticipatory readmission bypass.

The final plan still enters at fresh ScanPrice with target/stop rebuilt by
TradeBuilder and validated structurally. No proxy1.5% target/3% stop from the
study is imposed on the live plan, and no Win~ coefficients/ranking change.
The2% room checks reuse the support-room convention, not a fitted optimum.

## Evidence and verification

The [dated cohort study](../../../investigations/2026-10/investigation-2026-10-29-other-weekly-slope.md)
retains the original exploratory outcomes and coverage limitations. The new
policy is user-authorized, not independently validated Win/profit improvement.
The Stalled low/Daily-room guards further restrict its exploratory population.

Prepared ReversalRecoveryChecks use a frozen SOFI native prefix with no
October27 candles, actual EMA/MACD primitives, weak-but-available fresh H4,
unchanged local Stalled phase, boundary and rejection controls, and same-episode
completion. ReversalEntryChecks additionally builds a direct14.43 plan from
the frozen H4 with the real TradeBuilder. These are not full broker publication
replays. On2026-10-29 the user reported that the application build,
ReversalRecoveryChecks, ReversalEntryChecks and ReversalWatchListChecks all
passed. Codex did not execute them; detailed output/counts were not supplied.
User-run commands:

```powershell
dotnet build IbSwingTrader/IbSwingTrader.csproj
dotnet run --project tests/ReversalRecoveryChecks
dotnet run --project tests/ReversalEntryChecks
dotnet run --project tests/ReversalWatchListChecks
```

Assess subsequent entered Win/Loss/Open, profit and original rank1 outcomes.
