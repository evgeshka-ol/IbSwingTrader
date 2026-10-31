# Completed reversal impulses

Current admission rule, adopted 2026-10-19 after the CRGY review. Recognition
and readiness remain separate: a completed ReversalHook stays recognized but
cannot authorize a new ReversalHook plan from the same impulse.

## First Daily-mid return (2026-10-30)

New publication for every origin and both D1/H4 uses the
[Daily-mid/10% exit](REVERSAL_SCAN_PRICE_ENTRY.md). It therefore also rejects a
recognized episode whose first Daily-mid return is already proven by completed
native D1 history. This supersedes the former permission for DeepDeclineRecovery
to publish a continuation beyond mid. It does not remove diagnostic recognition.

Starting at the selected seed's confirmation (D1 date16:00; H4 start+4h), compare
each completed Daily bar with its own full-precision EMA20. A close at/above mid
proves return after confirmation, including a D1 seed's own close (AMD October28).
A high at/above mid is accepted only when the entire Daily bar starts at/after
seed confirmation. A seed-day wick can precede the trigger and is not sufficient
by itself. No final forming Daily bars are accepted. Earlier episodes do not
consume a newer seed. Existing post-BellUp and upper-band guards also remain.

The rejection reason contains `CompletionRule=ReversalDailyMidExitAlreadyReached`
and `ReversalDailyMidCompleted` with signal, confirmation, Daily evidence,
mean and proof source. A fresh quote already at/above the latest Daily mid is
rejected separately as `DailyMidAlreadyReachedByFreshPrice`. Weekly potential
does not reopen the completed first-return plan; a later independent signal
needs fresh admission. No weekly-holding execution profile is added here.

## Upper-band completion within an episode

Since2026-10-23 PostBellUpRecovery has an earlier **Daily-mid** completion
boundary; see [origin/completion rules](REVERSAL_ORIGINS.md). Its target is
the post-BellUp return to mid. The upper-band rule remains an additional completion guard and cannot
revive an episode consumed at mid under the current policy.

For each closed native bar from the recognized seed through the last completed
bar, compare its high with its own Bollinger upper and mid:

```text
Upper[t] > Mid[t] > 0 and High[t] >= Upper[t]
```

Equality counts. No near-touch percentage or price tolerance was introduced.
Use full-precision feature values; CSV/chart rounding is not a new threshold.
Use completed close as proof when high is unavailable. A touch before the seed
does not count. Search from the newest recognized seed, including its own bar:
an entire reversal impulse can occur within that bar.

Once any bar in that episode has touched upper, phase is Completed despite a
later pullback, moving upper band, or improving fresh H4 MACD. Completion is
reconstructed from the episode's closed history; it is not a permanent symbol
blacklist or a new lifecycle file. A new recognized seed has a new boundary.
The existing six-bar recognition window and seed rules are unchanged.

The older live/current-price upper test remains available for the local phase.
An unclosed quote/provisional candle alone cannot provide historical H4 evidence
for the cross-frame rule below. Completed native inputs are filtered at the
decision time; no future forming-bar high is accepted as historical completion.

## A completed H4 inside the Daily episode

Before selecting the preferred D1/H4 admission frame, if:

- D1 is recognized and its local phase is Active, MidCrossed or Stalled;
- H4 is recognized and Completed with a historical closed-bar touch;
- that H4 signal started at or after the current D1 signal bar's start;

then mark D1 admission phase Completed as well. This prevents the same impulse
from being admitted again solely because the last closed Daily bar still has
age zero, or because Weekly has a supportive regime. Publication rejects this
phase before checking weekly support or constructing a fresh admitted plan.
Stalled is included since2026-10-29 so the new anticipatory support cannot
readmit a completed impulse. See [gentle weekly support](WEEKLY_MID_SLOPE.md).

The temporal containment rule intentionally does not infer that an H4 episode
starting before the D1 seed belongs to the same impulse. An older completed H4
does not veto a newer D1 signal. A newly recognized active H4 has its own seed
and does not inherit a prior episode's completion. Normal BellUp precedence,
higher-timeframe support, price freshness and plan validity still apply.

## Saved evidence and reports

`PatternVerdictReason` and the reversal-episode log carry:

- `UpperReached`, `UpperTouchBarsAgo`, `UpperTouchHigh`, `UpperTouchUpper`,
  and `UpperTouchSource` for the local mid-bend phase;
- `ReversalImpulseCompleted(Timeframe, SignalBar, TouchBar, ConfirmedAt,
  High, Upper, Source)` for historical completion, including strict-hook seeds;
- `NotReady=H4-impulse-already-completed-in-D1-episode` when that evidence
  blocks Daily readmission, retaining its original local phase as context.

Confirmation time is native H4 start+4h or D1 date16:00. It is bar confirmation,
not an invented intrabar tick time. Completed sources are CompletedHigh or
CompletedClose. The evidence remains in Other and follows existing reason
preservation through evaluations and evaluation datasets. No new probability
model, score, or numeric report column was introduced.

For CRGY's saved October19 scan: Daily Friday high13.80 remained below the
saved Daily upper about13.87. H4 Friday12:00 high13.80 exceeded its upper
about13.51 and confirmed at16:00, after an H4 seed at08:00 inside that day's
Daily seed. This meets the new containment rule. The previous frozen candidate
and dated investigation remain unchanged. A full broker-backed replay was not
performed; the prepared checks exercise the policy and boundary conditions.

Source: ReversalImpulseCompletion.cs, ReversalHookPhaseClassifier.cs,
CandidateFinder.BuildReversalPhaseDiagnosis (initial and publication decisions).
No additional broker requests are made by this rule.

User-run checks:

```powershell
dotnet run --project tests/ReversalRecoveryChecks
dotnet run --project tests/ReversalWatchListChecks
```

Measure effects on later entered Win/Loss/Open, profit and missed opportunities;
a completed band excursion does not claim that later price cannot rise. FWDI's
renewed downward Daily-mid slope is a separate issue, not solved by this rule.
