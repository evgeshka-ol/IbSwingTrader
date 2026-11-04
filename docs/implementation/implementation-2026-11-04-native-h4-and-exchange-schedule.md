# Native H4 boundaries and exchange-time scheduling

Implemented2026-11-04 after the user's approval of the
[admission/Open audit](../investigations/2026-11/investigation-2026-11-04-admission-open-and-h4-grid.md).
The user requested both the H4 fix and scheduling in the exchange timezone.

## Resulting behavior

NativeH4Clock uses observed native start timestamps available at decision time.
Known per-date phase and the most recent UTC phase project through each date's
America/New_York offset. Real next starts establish interval ends; configured
session close caps shortened bars. The saved broker grid04/08/12/16 in summer
becomes04/07/11/15/19 in winter, with04–07 and19–20 stubs. Candles/timestamps
are not shifted or resampled. Future starts cannot supply phase evidence.

The clock is shared by completed/current H4 selection, BellUp forming-M5 timing
and native adjacency, ReversalH4Momentum's expected previous bar, upper/mid
completion, watch-hook confirmation, optional diagnostics, M5 bridges and
prepared-history validity. CandidateFinder provides existing trading dates/
session closes so holidays and configured early closes are respected. Genuine
missing previous bars still reject. An H4 range request keeps its actual end
cutoff;07:30 no longer rounds back to04:00. A bounded start overlap covers the
possible native bar containing the requested start.

Native H4 exports previously merged04:00 and07:00 into one local four-hour
bucket. Scanner, cache snapshots, dataset fallback and explicit report-normalizer
exports now preserve exact native starts. Formulas and window lengths remain;
old nonempty saved candidate series and closed outcomes are not automatically
replayed. Shared Daily16:00 confirmation, targets, stops and all readiness
thresholds remain in force.

Windows scheduling accepts AtExchange, default04:15/16:15 ET. It creates daily
UTC candidates for both exchange offsets; the wrapper admits only the configured
exchange slot within five minutes. Exchange weekday filters and morning evaluation
window04:00–12:00 use invocation ET. Existing locking and process checks remain;
slot claims prevent duplicate execution and automatic retry after a failure.
The computer timezone is not changed. Inactive season candidates do not start
application/broker processes. Fixed ET times do not shift to an early-close offset.

The existing task must be re-registered by the user on Windows to replace old
local triggers and arguments. Register-ScannerSchedule uses the same default
TaskName with Force. WhatIf builds the proposed task without registering it.
Old AtLocal/MorningStartLocal/MorningEndLocal arguments are retired. A task's
NextRunTime may refer to an inactive UTC candidate; the configured scanner slots
remain04:15/16:15 ET. See the [updated guide](../guides/scanner-schedule.md).

The evaluation log now distinguishes Plans/Entered/plan Win/Loss/Open/NoEntry
from DiagnosticNoEntry, preventing hundreds of zero-plan Other from looking
like failed entries. Dataset rows and outcome semantics are unchanged.

## Verification prepared, not run by Codex

- NativeH4Checks: summer/winter phase, observed alternative/per-date phase,
  future exclusion, shortened opening/trailing bars, Monday after DST, previous
  winter evening, configured early close/holiday, exact scanner H4 export and
  available fresh H4 momentum. The frozen SOFI prefix includes native H4 through
  November3 11:00 plus M5 through17:15, decision17:20; no final15:00 native close
  is input. Real closed11:00 and forming15:00 are adjacent.
- ScheduledScannerChecks: two PowerShell syntax parses, four UTC candidates,
  summer/winter and unequal US/Europe DST weeks, host-zone independence,
  exchange weekday, wrong UTC candidate, five-minute window, mocked process
  ordering, failures, midnight, disable flag and slot deduplication. No real
  app/broker process or ScheduledTasks registration is used by these checks.
- Existing scanner, reversal, body-diagnostic, dataset and output checks cover
  compatibility with the shared helper and unchanged execution policies.

Codex reviewed project XML/source paths, fixture timestamps, links and focused
diffs. Builds, C# tests, PowerShell tests, scans and Windows task registration
were not run. No successful runtime verification is claimed for this revision.

User-run commands from repository root:

```powershell
dotnet build IbSwingTrader/IbSwingTrader.csproj
dotnet run --project tests/NativeH4Checks
dotnet run --project tests/ScannerTimingChecks
dotnet run --project tests/ReversalRecoveryChecks
dotnet run --project tests/ReversalWatchListChecks
dotnet run --project tests/BellUpBoostExitChecks
dotnet run --project tests/BellUpUpperBandChecks
dotnet run --project tests/ReversalLowerBandBodyChecks
dotnet run --project tests/EvaluationConfidenceChecks
dotnet run --project tests/CandidateCsvChecks
.\tests\ScheduledScannerChecks.ps1
```

After successful checks, preview and apply the exchange schedule:

```powershell
.\tools\Register-ScannerSchedule.ps1 -AtExchange '04:15','16:15' -WhatIf
.\tools\Register-ScannerSchedule.ps1 -AtExchange '04:15','16:15'
```

TWS must be available for actual scheduled runs. Compare subsequent final unique
candidate counts, admission reasons, entered outcomes and profits; the original
42/16 rejected BellUp counts are audit evidence, not guaranteed new admissions.

Current contract: [native clock](../project-skills/ibswingtrader-scanner/references/NATIVE_H4_CLOCK.md).
Task Scheduler API reference: [Microsoft StartBoundary](https://learn.microsoft.com/en-us/windows/win32/taskschd/trigger-startboundary).
