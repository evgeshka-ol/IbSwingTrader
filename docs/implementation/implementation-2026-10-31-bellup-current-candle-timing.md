# BellUp Recent boost: latest observed candle

Implemented2026-10-31 at the user's direction after CDNA30 October:
adding the observed forming candle shifts the previous closed boost into
history, so it no longer remains the checked candle throughout the new H4.

BellUpEntryTiming.IsReady accepts a separate BellUpTimingObservation built
from the completed M5 prefix available at its exact decision time. The shared
GetCompletedCandles contract is unchanged. Existing cached final forming native
candles cannot become scan-time inputs. No current indicators/classifier/rank
are substituted: this patch changes only range timing's checked observation.

Current H4 uses native04/08/12/16 intervals. Daily uses the forming regular
session09:30–16:00. Five-minute grid coverage must extend from the interval opening
slot through the latest completed slot, with valid OHLC and no missing slots.
Zero-volume slots cover time but their extremes do not become traded OHLC.
Open is the first actual traded open, including after known empty slots.
All-zero, unknown-open, incomplete-grid and future evidence cannot create a
current candle. Missing current input falls back to the prior closed-only rule.
The first five minutes have no completed M5 yet; no observation is invented.

With a current observation the baseline is the preceding N native closed
ranges. Green and range>=multiplier*median still veto; N=5, multiplier=2 remain.
This also applies to the confirmed-Daily timing veto, preserving the existing
H4-continuation override. Pattern recognition, confirmed lower-band turn,
body-based boost exits, ReversalHook and price/structure guards remain separate.
A boosting current candle may still be rejected: early impulse versus exhaustion
is not inferred from range alone. The user raised that remaining limitation
while this scoped change was being implemented; no new stage threshold was
silently introduced.

CandidateFinder loads one bounded current-M5 window for recognized live-source
BellUp contexts before timing can reject them. Same-ticker data can be reused
within the same five-minute request bucket. At publication the existing fresh
M5 request is extended to cover the interval start; the partial is rebuilt and
selected-frame timing rechecked before the plan is built. This prevents an old
ready partial from silently authorizing a later current boost. Native history
is frozen at its acquisition time; missing intervening H4 history rejects a
later interval. No pending BellUp list, daemon or extra scheduled command is added.
Reasons preserve State=Forming, exact decision/coverage times, source and OHLC.

## Prepared verification

ScannerTimingChecks now covers the actual range model and forming observations;
its former body-based assertions predated the October7 range-policy change.
The frozen CDNA fixture contains only six native H4 through29 October16:00 and
seven M5 through30 October04:30. At04:36:52 these give current
O81.95/H83.8817/L81/C82.55. Closed-only input rejects the prior boost; this
observed current input clears that timing veto. The eventual pause low76 and
second boost are excluded from the fixture. This does not assert that final
publication would pass price freshness or that the hypothetical trade won.

Other prepared checks cover current-boost exact threshold, red current candle,
future/forming cache exclusion, interval/session boundaries, wrong decision,
missing grid/open/tail, all-zero volume, carry extremes, Daily RTH, insufficient
history and source immutability. Existing boost-exit and reversal checks retain
the closed-candle helper semantics.

Codex inspected XML/source links, fixture prefixes, Markdown links and focused
diffs. Codex did not run application commands, broker scans, builds or C# tests;
the user's subsequent verification is recorded below.

User-run commands:

```powershell
dotnet build IbSwingTrader/IbSwingTrader.csproj
dotnet run --project tests/ScannerTimingChecks
dotnet run --project tests/BellUpBoostExitChecks
dotnet run --project tests/ReversalWatchListChecks
```

Current contract: [Series Playbook](../project-skills/ibswingtrader-scanner/references/SERIES_PLAYBOOK.md).
Original evidence: [CDNA/TMUS audit](../investigations/2026-10/investigation-2026-10-31-cdna-continuation-tmus-near-exit.md).

## User-run follow-up

The user reported `Passed192 reversal watch checks` for ReversalWatchListChecks.
BellUpBoostExitChecks then exposed stale test expectations: an episode boundary
must exclude the older boost while preserving the supported single-boost exit
inside the current episode. The check now verifies that exact boost/target and
separately rejects borrowing boosts when none remain after the boundary. A
single-boost late-penalty fixture also contained two historical boosts and was
corrected to represent its stated scenario. These are test corrections; the
production exit/penalty algorithms were not changed.

After those corrections, the user confirmed BellUpBoostExitChecks passed, then
confirmed the remaining listed checks and application build also passed:

- `dotnet build IbSwingTrader/IbSwingTrader.csproj`
- `ScannerTimingChecks`
- `BellUpBoostExitChecks`
- `ReversalWatchListChecks` (192 checks reported)

These are user-reported successful runs, not runs performed by Codex. They
validate the build and regression checks; later scan admission and trade
performance still require observation on new data.
