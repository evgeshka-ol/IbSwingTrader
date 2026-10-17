# BellUp lower-band turn veto

Implemented at the user's explicit request after the INTC H4 example,
following SECZ H4 and PURR Daily. This is an operational rule, not a claim
of validated predictive improvement. Earlier experiments found weak/no
standalone discrimination for lower-band rises and possible false vetoes
of later continuations. Those results remain valid limitations.

## Exact condition

On the confirmed BellUp timeframe (Daily or canonical broker H4), use only
completed candles available at the scan timestamp. Use the same historical
prefix classifier and continuous-confirmation episode boundary as the
two-boost exit. Do not mix timeframes or borrow a trough from an old episode.

1. Find the minimum lower-band value in the current episode; for a flat
   minimum, take its last point.
2. Require a decline into that minimum within the episode, allowing equal
   rounded points at the minimum.
3. Require both last completed lower-band values to be strictly above it.

The latest value need not exceed the previous one: the user's condition is
that both are above the trough. A return to the minimum, a new low, or a
new classification episode prevents the old trough from triggering this
rule. A series rising from its first episode point without a preceding
decline is not rejected by this veto. Missing/zero bands or unaligned
arrays do not create a signal.

No actual decrease of upper-minus-lower width is required. This rule treats
the lower-band turn as sufficient reason not to enter even while total
width is still growing. It does not close existing positions.

## Integration

`BellUpLowerBandTurn.TryFindConfirmedTurn` implements the condition.
`CandidateFinder.IsBellUpPhaseReadyToday` applies it after the existing
two-candle boost veto. Rejected candidates follow the existing Other/zero
plan path. ContextNotes/logs include timeframe, minimum value and candle
timestamp, and the two latest completed values/timestamps.

`BuildBellUpHistory` is shared with the two-boost exit, so both rules use
the same completion convention and episode boundary. Exit calculation for
remaining eligible BellUp candidates is unchanged. No CSV history or
evaluation labels were rewritten; the new rule affects future scans only.

## INTC and timing caveats

The September 10 06:42:27 saved H4 lower-band tail includes:

    ... 84.77, 83.35, 82.63, 82.34, 82.85, 82.93, 83.20, 83.80

This numeric sequence satisfies the condition. The exact admission result
also depends on the current shared classifier's continuous episode, and
must be confirmed on a user-run scan. No claim is made that Yahoo's 17:00
label is identical to the broker's bucket label.

For SECZ/PURR, a single higher completed point alone will not fire this
two-point rule. It becomes eligible to fire only after two completed
points are above the trough; later chart candles cannot be backdated into
an earlier scanner decision.

## Verification

Extended `tests/BellUpBoostExitChecks` with 12 lower-turn assertions,
including the INTC numeric sequence, first rise only, flat bottom, new low,
equal latest value, nonmonotonic recovery, old episode and invalid inputs.
There are now 28 combined assertions. Completion filtering is covered by
the existing Daily/H4 checks in that project.

Run as user:

    dotnet run --project tests/BellUpBoostExitChecks
    dotnet run --project tests/ScannerTimingChecks

Builds, checks and scanner were not executed by Codex under the project
responsibility boundary. Static diff whitespace checks passed.
