# H4-led support below the Daily mid

Current readiness policy, adopted 2026-10-21 at the user's request following
RCUS and SMCI admission investigations. It adds an alternative to the old
Daily support branch; it does not redefine recognition or ranking.

## Why the former gate missed early hooks

The old H4 admission required supportive D1 BellUp, or D1 mid slope>=-0.20%,
an improving nonnegative D1 histogram and at least2% room to D1 mid. D1 Down
was an unconditional rejection. Consequently an active H4 reversal could
be rejected until the slower Daily average/MACD caught up. SMCI's October20
evening scan was already MidCrossed, but D1 slope-3.23% and histogram-1.42
failed support. RCUS had a separate early-phase issue plus D1 Down.

## Alternative H4 support

Publication may accept selected **H4** episodes despite D1 Down or negative
Daily MACD only when all of these hold:

1. Recognition survives the fresh publication refresh and phase is Active
   or MidCrossed. Existing upper-touch completion takes precedence.
2. D1 regime is not Collapse. Its mid is valid, price is below it and the
   remaining room `(D1Mid / freshPrice - 1) * 100` is at least2%. This is
   the existing room threshold, not a newly fitted profit target.
3. Completed native H4 lower band is rising strictly. H4 mid is flat/rising,
   or its latest negative delta is strictly less negative than the preceding
   delta. A renewed downward acceleration cannot qualify.
4. Latest closed H4 histogram strictly exceeds its preceding value; it may
   still be negative.
5. Fresh provisional H4 histogram also improves over the previous closed
   H4 histogram. Reuse the same quote and momentum evidence that produced
   the phase diagnosis, including native-session, age, expected closed-base
   and timestamp checks in ReversalH4Momentum. No historical-price fallback.

The new branch uses full-precision feature values from the completed native
prefix, not rounded CSV tails. EMA calculation retains its complete available
history; taking the last three feature points does not restart its seed.

Daily MACD positivity and the D1 mid-slope floor are not required by this
alternative. If it does not qualify, the original Daily-support path still
applies, including its Down/Collapse checks. D1 hooks retain their existing
Weekly support rules; BellUp precedence remains unchanged. This does not
admit every recognized hook or every quote above its last closed price.

Publication uses refreshed completed native D1 series for both this support
decision and the exported Daily indicator rows, instead of leaving the
support calculation on H4-aggregated Daily series while exporting native D1.
No additional broker request is introduced. The existing D1 fallback remains
when native history is absent.

## Evidence and verification

Accepted reasons include `Support=H4-led recovery below D1 mid`, room,
H4 mid/lower deltas, closed histogram and fresh momentum provenance.
Rejected alternative checks append a specific `H4-led recovery unavailable`
reason to the ordinary unsupported/Down rejection. Published plans still
require valid fresh entry/target/stop; no score or Win probability is added.

Prepared regressions in ReversalRecoveryChecks cover RCUS-like negative
histogram recovery, SMCI-like mid-crossed recovery, Collapse, inactive and
completed phases, missing recognition/history, weak/unavailable fresh
momentum, deteriorating closed momentum/bands and room boundaries. Existing
CRGY completion and fresh quote guards remain in that suite. Codex has not
run builds/tests/application or performed a full historical publication replay.

User-run checks:

```powershell
dotnet build IbSwingTrader/IbSwingTrader.csproj
dotnet run --project tests/ReversalRecoveryChecks
dotnet run --project tests/ReversalWatchListChecks
```

This policy expansion is not empirically calibrated. Validate on subsequent
entered Win/Loss/Open, profit and rank results. RCUS and SMCI source-level
support examples are not claimed replayed admissions or guaranteed winners;
historical zero plans and evaluated NoEntry rows remain unchanged. See the
dated RCUS/SMCI investigations for original decisions and data limitations.
