# Empty wishlist and CRGY/FWDI admission — 2026-10-19

## Evidence and scope

Source: `Data/logs/log-20261019_0300.log`, completed 04:51:32 ET;
`Data/Tickers/candidates.csv`, scan published at 04:50:32. This is inspection
of saved scanner evidence and source code, not a new broker scan or evaluation.
No claim about subsequent FWDI outcome is made. Current groups are BellUp,
ReversalHook, Other; current mid is EMA20.

## Why the wishlist became empty

187 active legacy rows loaded at log line15. All187 were retired with
`Reason=hook-observed` during cached history inspection (lines131–25889).
This was not removal by a fresh shallow/above-mid price check. The migration
logged28,854 historical hook observations across inspected symbols.

TOP_OPEN_PERC_LOSE returned0, TOP_PERC_LOSE returned50. Of the latter,49
enrollment checks between03:07:31 and03:15:21 failed with no-fresh-M5-price;
XOM failed historical loading after repeated empty chunks/timeouts (line29316).
No new price/depth enrollment check succeeded. For JMKE, the03:07:30 request
returned candles but its fresh-window filter produced Bars=0 (lines28189–28194).
Configured premarket starts04:00, after these attempts. Later ranking ran into
04:50 without retrying enrollment. Consequently empty CSV did not establish
that no ticker met the5% depth criterion.

The adopted fix is deferred enrollment with persisted observations, bounded
M5 retries at ordinary scan checkpoints and original sighting timestamps.
The current contract is in the scanner's REVERSAL_WATCH_LIFECYCLE reference.
No generated watch/candidate/cache files were manually repaired.

## CRGY: Friday signal selected again on Monday

Published ReversalHook, saved price13.68, entry13.52, target14.20, stop12.85.
Daily bend: October16; latest completed Daily: October16; BarsSinceBend=0,
Phase=MidCrossed, Daily mid13.22052643395486. H4 bend October16 08:00,
latest closed H4 October16 16:00; Phase=Completed, BarsSinceBend=2.
Support reason: W1 BellUp support.

Friday's Daily O12.57/H13.80/L12.54/C13.77 followed Thursday close12.51:
about10.07% close-to-close and9.55% open-to-close. Monday saved price is
below Friday close. This corroborates the user's observation that the initial
reversal move occurred Friday; it does not establish Monday's later outcome.

`BuildReversalPhaseDiagnosis` prefers any recognized Active/MidCrossed frame
over Completed/Stalled on the other frame. The last completed D1 being Friday
makes its bar age zero on Monday; this is not elapsed-time freshness or proof
of a new Monday trigger. The chosen Daily episode can pass higher-timeframe
weekly support despite a Completed H4 episode. The source does not currently
separate new reversal entry from continuation of that completed shorter-frame
move. No automatic two-frame veto was added from this one example.

## FWDI: deceleration is not a confirmed upward EMA turn

Published ReversalHook, saved price6.95, entry6.33, target6.96, stop6.14.
The plan's target is measured from its proposed pullback entry, not6.95.
Daily bend: October15; latest completed Daily: October16. Phase=Active,
BarsSinceBend=1, Recovering=True, Daily mid7.30137853356779. H4 Phase=Stalled,
BarsSinceBend=5, mid7.05996950516305. Support: W1 BellUp support.

Daily mid delta tail, October13–16:

```text
-0.02454181, -0.06029973, -0.02027119, -0.04119774
```

October15's decline slowed; October16's became steeper again. The Daily
mid did not turn upward. Daily closes6.79 ->7.15 ->6.91; the last close still
exceeded the pre-bend6.79, and the Daily MACD histogram was slightly better
than at the bend (-0.19868377 versus -0.20132323). This satisfies the existing
recovery test despite the renewed mid decline. H4 histogram was positive
(last closed0.02663421; provisional0.02892995), while the Daily histogram
remained negative. These are distinct timeframe observations.

`ReversalHookPhaseClassifier` admits a bend from three negative EMA deltas
followed by a sufficient discrete deceleration and improving price/histogram.
It does not require EMA slope to become positive. Once a bend exists,
recovery can remain true based on price above pre-bend close and histogram
above bend value; renewed EMA deterioration alone does not stall it.
Frame selection then prefers Daily Active over H4 Stalled, with weekly
support. This explains admission without relying on a future Win/Loss.

## Follow-up hypotheses, not adopted filters

Distinguish an initial deceleration, an upward turn, a resumed decline, and
continuation of a previously completed move. Investigate their subsequent
entered Win/Loss/Open, profit and rank, retaining scan-time features. Avoid
hardcoding CRGY/FWDI or requiring all mid slopes positive without checking
early reversals lost by that rule. The VSTM fresh-H4 recovery change addresses
a different stale-Daily-MACD veto; do not silently revert it.

Prepared user-run checks for the deferred enrollment fix:

```powershell
dotnet run --project tests/ReversalWatchRetryChecks
dotnet run --project tests/ReversalWatchListChecks
```

Builds, tests and application commands were not run by Codex.
