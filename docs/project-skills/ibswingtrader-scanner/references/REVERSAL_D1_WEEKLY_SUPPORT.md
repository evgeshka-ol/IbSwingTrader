# Daily deep-decline hooks during weekly recovery

Current publication policy, adopted2026-10-28 following NN's October23 scan.
It adds a bounded alternative to Weekly support for selected D1 episodes.
Since2026-10-29 there is also a separate [gentle weekly-mid support rule](WEEKLY_MID_SLOPE.md),
including a bounded anticipatory Stalled path. That geometric rule can operate
on Monday; it does not change the same-week comparison defined here.

## Same-week observation, not week-over-week crossover

NN's saved W1 histogram worsened from approximately-0.56 to-0.63 between
weeks, while the current week's MACD improved during Friday's Daily recovery.
The former W1 Down veto ran before MACD checks and rejected the active D1
episode. Improvement within one week and improvement against the preceding
week are different observations.

The new exception requires all of the following:

1. A recognized D1 Active/MidCrossed episode with native-D1 origin
   DeepDeclineRecovery. PostBellUpRecovery and Unclassified cannot use it.
2. W1 regime is not Collapse. Down direction alone may be overridden.
3. Chronological positive completed native D1 history, with at least26
   weekly observations. The latest two D1 observations must be in the current
   exchange week; no final forming-Daily close or future bars are accepted.
   Monday therefore cannot use this same-week comparison.
4. D1 MACD histogram improves; fresh price has at least2% room to the native
   D1 EMA20 mid. Completed H4 histogram and the phase's fresh provisional H4
   histogram both improve. The existing H4 quote/session/freshness guards apply.
5. Construct weekly closes from the complete available native D1 prefix.
   Compute current-week MACD, then replace only that week's close with the
   preceding completed D1 close and recompute. Both MACD line and histogram
   must strictly improve. Older weekly closes stay fixed in both calculations.
   Negative MACD is allowed; improvement over the preceding week's histogram
   is not required. This is a native-D1 weekly calculation, not a restatement
   of the older H4-aggregated rounded exported weekly series.

Source: `ReversalDailyWeeklySupport.cs`, called in the final publication
refresh before the W1 Down rejection. No new broker request, extra CSV column,
ranking score or probability is introduced. Acceptance records both observation
times, week start, decision time, old/new line and histogram, Daily momentum,
remaining room and fresh H4 provenance in the existing PatternVerdictReason.

Existing BellUp precedence, upper-touch and PostBellUp completion checks run
first. Fresh plan entry/target/stop checks still run after support. Passing
support does not establish an executable entry, a Win or a guarantee of the
subsequent maximum. The original NN zero plan and NoEntry remain historical.

## Verification

ReversalRecoveryChecks contains a frozen250-bar native NN D1 prefix ending
October23, plus saved closed/fresh H4 momentum evidence. It covers admission
support, Collapse, non-deep origins, inactive phases, missing/weak H4 momentum,
insufficient room, forming/future Daily data, another week, short history,
Monday, renewed decline, chronology and immutable inputs. This is support
verification, not a full publication/trade replay or calibrated edge study.

The user runs:

```powershell
dotnet build IbSwingTrader/IbSwingTrader.csproj
dotnet run --project tests/ReversalRecoveryChecks
```

Validate subsequent entered Win/Loss/Open, profit and confidence outcomes.
The October26 peak was not used to calculate the October23 support evidence.
