# RCUS: early recovery masked by closed H4 pause

Recorded 2026-10-21 from the 2026-10-20 morning scan, ScanTime
04:19:40 America/New_York. Sources: log-20261020_0300.log,
candidates.csv, evaluation-dataset.csv and Data/cache/RCUS.json.
Application, builds, tests and broker commands were not run by Codex.

## Saved decision

RCUS was recognized on H4 with a bend on October 19 at 08:00, age two
completed bars. Publication retained Other with Phase=Stalled and zero
EntryPrice/StopLoss/ExitPrice. This was an admission rejection, not an
unfilled executable plan.

The refreshed closed H4 closes were 22.21, 22.15, 22.14, with mid
22.7226585923. Fresh price 22.26 at 04:07:32 exceeded these closes but
remained below mid. Closed histogram improved to -0.00130027987;
fresh provisional H4 histogram was +0.02297178577. Existing recovery
required the latest closed price above the last-three minimum, which failed.
Its fresh-price override only applied after reclaiming mid.

The initial diagnosis at 03:51 was Active using an earlier completed prefix;
publication refresh at 04:07 changed it to Stalled. Publication at 04:19:41
preserved that rejection. Quote freshness at refresh does not establish a
new fresh executable quote twelve minutes later.

## Narrow phase correction and remaining gate

The source change adds fresh early recovery below mid for an established
bend: fresh improving H4, price strictly above the last three closed prices
and pre-bend close, and closed histogram no worse than at the bend. It
does not change recognition seeds, upper completion or higher-frame gates.
Prepared regression cases include RCUS-like recovery and negative controls.
They have not been executed by Codex.

RCUS also had D1 Neutral/Down, with mid 24.1500770813 above its last
close 22.14. Existing higher-timeframe support rejects Down. Thus repairing
the phase does not establish that this ticker would pass full admission.
Removing that independent support gate needs broader outcome evidence.

## Subsequent path and amplitude interpretation

The saved report's 37.22% was the extrema range 18 to 24.70. ScanPrice
was 22.26; the later maximum represents 10.96% from that reference.
The maximum before the 08:35 plunge was 22.70, about 1.98% above ScanPrice.
The 08:35 M5 was O22.60/H22.60/L18/C18.24, followed by a rebound reaching
24.70 at 10:20. The earlier recovery and the post-plunge rebound are
different episodes; the latter was not observable at morning publication.

A preliminary diagnostic plan at 03:51 used Entry20.59/Stop19.97/Exit21.83.
It was not the final admitted publication plan. All three levels occur
inside the 08:35 parent M5; no M1 is cached. Its high can precede entry,
while its close is below stop. This cannot substantiate an after-entry Win
under the evaluator's conservative first-entry-bar rules. No actual broker
execution is inferred.

## Morning cohort audit

Among 145 published rows: BellUp9, Other136, no admitted ReversalHook.
109 Other reasons preserved recognized ReversalHook: Stalled74,
MidCrossed6, Active19, Completed10. Twelve later had old range amplitude
at least 10%, including RCUS, SMCI, CHPT, SPCX, CRWV, HUT, BLZE, AXTI,
BLSH, ALM, NBIS and IMMX. These are diagnostic opportunities, not wins.

RCUS and AKAM matched the specific saved-data combination: selected H4
Stalled, improving fresh H4, price above the last three closed prices but
below mid. This is an audit of saved series, not a full publication replay
or validation of the rule's profitability. Dated figures here retain the
amplitude definition used by the inspected report.

User-run verification:

```text
dotnet run --project tests/ReversalRecoveryChecks
dotnet run --project tests/ReversalWatchListChecks
```
