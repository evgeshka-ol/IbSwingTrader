# Report ordering, morning evaluation and closed D1 history — 2026-10-20

User-authorized changes, including the clarification that group precedes
confidence. Current groups remain BellUp, ReversalHook, Other.

## Presentation

Candidate CSV order is ScanTime descending, group in the above order, saved
DiagnosticsEstimatedHitRatePct descending numerically, then DisplayRank/ticker.
Missing confidence follows zero. Group DisplayRank, console and evaluation
dataset rank reconstruction share CandidateDisplayOrder. Existing internal
quality/profit scores break confidence ties. Assign the common publication
ScanTime before sorting the current console groups, so varying detection times
do not defeat confidence ordering. Existing admission thresholds and probability
coefficients were not retuned.

Offline `sort-candidates` uses the existing CSV normalizer to rewrite the saved
candidate report with this order, without a broker scan or feature recalculation:

```powershell
dotnet run --project IbSwingTrader -- sort-candidates
```

## Scheduled evaluation

Invoke-ScheduledScanner.ps1 chains evaluate-candidates only after a successful
scan invoked in the local06:00–12:00 window, by default. Actual wrapper logs
show starts10:00 and23:15 FLE Standard Time. Invocation-time classification
avoids treating a long evening scan as morning when it ends after midnight.
The same lock spans both processes; separate evaluation stdout/stderr and
per-stage status preserve failure attribution. Existing task actions invoking
this runner inherit its defaults. Registration exposes the window and an opt-out.
No Windows task was registered or launched by Codex.

## Why the October19 aftermarket wishlist remained empty

Source: Data/logs/log-20261019_1615.log, not a newly run scan. It loaded zero
active watches, retained67 pending observations, logged201 retries and292
deferrals for missing-or-stale-closed-D1. Example OCUL native D1 cache ended
October16; its16:15:17 request ended October19 00:00 rather than16:15.
All checks expected the completed October19 D1 and could not reach the depth
test. The lifecycle ledger is consumed-cycle memory, not an active watch list;
pending records are unvalidated observations, not qualifying drops.

HistoricalDataService now preserves the actual D1 range end at/after16:00;
before close it keeps the midnight boundary. D1 starts still normalize to the
date. Pending checks refresh a bounded missing Daily tail if prepared/cache
data predates a close, merge it with warmup history and inspect completed
Daily hooks before using the refreshed mid. The same fresh-price5% threshold
and lifecycle checks remain. This removes a demonstrated data blocker; it does
not guarantee new wishlist entries or ReversalHook admissions.

Checks prepared, not executed by Codex:

```powershell
dotnet run --project tests/CandidateCsvChecks
dotnet run --project tests/DailyHistoryChecks
dotnet run --project tests/ReversalWatchRetryChecks
.\tests\ScheduledScannerChecks.ps1
```

Windows wrapper validation remains user-run. Review registration via -WhatIf
if changing parameters. At the next morning invocation, verify the shared
status log's Starting evaluate-candidates and Evaluation completed exit code.
