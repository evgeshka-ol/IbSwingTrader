# Native H4 intervals and exchange-time scheduling

Adopted2026-11-04 after the post-DST admission audit. Native bar starts and OHLC
remain the source evidence; fixed local04/08/12/16 and universal start+4h are no
longer used for scanner completion/current-interval admission.

## Interval resolution

`Common/Time/NativeH4Clock.cs` uses only native starts observed by the decision
cutoff. The opening04:00 stub is excluded from full-bar phase inference. Known
per-date UTC phases take precedence; the most recent observed full-bar phase
projects into dates with no full-bar observations using America/New_York's
UTC offset for that date. The normal no-evidence fallback phase is UTC four-hour
boundaries; live admission still needs sufficient native history.

For this broker's saved2026 prefixes, observed UTC phase is stable across DST:

| Exchange date | Native starts | Opening interval | Final interval |
|---|---|---|---|
|October30|04,08,12,16|04–08|16–20|
|November2/3|04,07,11,15,19|04–07|19–20|

An observed next same-day start within four hours establishes the preceding
bar's boundary. Otherwise the projected opening boundary or ordinary four-hour
bound applies, capped at configured session close. CandidateFinder supplies
trading dates/session closes from its existing calendar; early-close bars are
clipped to that close. Bars starting outside an explicitly closed session
cannot become completed before their own start. Future native starts are not
phase evidence. Native timestamps and stored candles are not shifted/resampled.

The same clock serves shared GetCompletedCandles, BellUp current M5 aggregation
and native adjacency, fresh ReversalH4Momentum/expected last closed start,
reversal upper/mid completion, M5 bridge start, in-run history validity and
optional diagnostics. Watch hook confirmation also uses native boundaries.
The shared Daily16:00 convention and all pattern/price/risk thresholds remain.

Native H4 is already bucketed. Scanner exports, cache snapshots, dataset fallback
series and the user-run report normalizer now deduplicate exact native starts,
so winter04 and07 no longer collapse into one local four-hour export bucket.
Feature formulas/window lengths are unchanged. Existing nonempty saved candidate
series and historical outcomes are not automatically reconstructed by this patch.

H4 history range requests keep their actual end cutoff instead of rounding07:30
back to04:00. Start has at most four hours of conservative overlap so the native
bar covering the request start is retained before its phase is known. Completion
selection remains a separate as-of decision; returned forming values do not
become completed merely because they were fetched.

## Scheduler

Windows registration accepts `-AtExchange`, default04:15/16:15 America/New_York.
It creates daily UTC candidate triggers for both exchange offsets. The wrapper
converts invocation UTC to ET, admits only the configured slot within five
minutes, checks any weekday restriction in ET, and claims that date/time slot
under the existing shared lock. Opposite-season candidates exit without starting
the app. A claimed slot is not retried automatically after failure.

Morning evaluation uses invocation ET04:00–12:00, even if the scan finishes later.
The computer's timezone is not changed. Registration must be rerun to replace
old local triggers/action arguments; see the [Windows guide](../../../guides/scanner-schedule.md).
Task Scheduler may show the next unused UTC candidate rather than the next
actual scanner start. Fixed ET slots do not automatically move on early-close days.

The UTC trigger mechanism uses explicit StartBoundary timezone information:
[Microsoft Trigger.StartBoundary](https://learn.microsoft.com/en-us/windows/win32/taskschd/trigger-startboundary).

## Validation and limits

NativeH4Checks freezes SOFI native H4 through November3 11:00 plus complete M5
15:00–17:15 (as-of17:20). It verifies native11 closes at15, the current interval
starts15, and fresh H4 evidence is available. The eventual15:00 native close is
not input. Summer/winter boundaries, short stubs, holidays/early close, observed
alternative phase, cache exports and future exclusion are separate controls.

ScheduledScannerChecks covers DST dates, unequal US/European DST weeks, host-zone
independence, wrong UTC candidates, morning/evening chain, failures, midnight,
disable flag, deduplication and PowerShell syntax without real app/broker calls.
Codex did not run these C#/PowerShell tests, builds or Windows registration.
Later participation and trade outcomes must be measured after user deployment.
