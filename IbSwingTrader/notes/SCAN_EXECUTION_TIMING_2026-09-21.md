# Execution price and publication timing

MSTR's September 21 plan used 153.91 (September 18's H4-derived daily
close) while the CSV displayed the newer M15 reference 162.49. BellUp's
existing no-discount policy now uses the same live reference for its entry.
Reversal entry policy remains unchanged.

After deduplication, playable BellUp plans request fresh M5 history directly
from TWS with an exact request cutoff. Higher-ranked candidates refresh last
to shorten their wait before display. This is an explicit snapshot request,
not the ordinary history method that rounds its endpoint to a bar boundary.
Completed M5 bars from first detection onward are merged into the cache;
forming bars are not newly persisted as completed history.

The latest partial M5 close, or the immediately previous completed M5 close
when no partial bar is returned, supplies the reference. Older bars cannot
silently become a live reference. Entry, stop, stop-limit, percentages and
boost-based exit are rebuilt together using the existing signal context and
M15 execution history. This refresh does not rediscover the pattern from new
H4/Daily candles. The existing live-H4-mid invalidation check is repeated.
Failure to refresh moves the row to Other with a zero plan and diagnostics.

## Saved timestamps and snapshots

All times are exchange-local, as identified by ScanTimeZone.

- ScanTime retains its legacy batch/group identity at output start.
- RunStartedAt is the scan invocation time.
- FirstSeenAt is the receipt time of the first broker preset containing the ticker.
- SignalObservedAt is the time the selected context was assembled.
- PublishedAt is captured immediately before printing the row.
- DetectionToPublicationSeconds and PriceToPublicationSeconds expose actual delays.
- TradePlanReferencePriceTime describes the price cutoff (or completed bar end).
- TradePlanReferencePriceBarTime records the source bar start. Ordinary cached
  M15 history has no exact as-of timestamp, so ReferencePriceTime stays null
  until a fresh M5 snapshot is obtained; receipt time is not a market-data time.
- TradePlanReferencePriceObservedAt describes receipt of the price data.
- TradePlanReferencePriceSource distinguishes M15 history, M5 partial/complete,
  and indicator fallback.
- TradePlanInitialReferencePrice/Time/BarTime/ObservedAt and InitialEntryPrice preserve
  the first plan's baseline before refresh; PlanBuiltAt dates the new plan.
- TradePlanPublicationRefreshStatus records Refreshed or Unavailable.
- TradePlanM5PreviousBarTime/Open/Close and M5CurrentBarTime/Open/Close preserve
  the inputs to M5ProjectedEntryPrice. This is current open plus the preceding
  green body's size, rounded to cents. Missing/nonadjacent bars or a nonpositive
  previous body produce no forecast. The forecast is diagnostic only.

New CSV fields are nullable so legacy rows are not assigned invented times.
Evaluation retains ScanTime as its join key but starts no earlier than
PublishedAt. Its existing M5 policy excludes the bar straddling that instant,
so it cannot assert an intrabar fill before publication. Retry reconstruction
preserves this start via EvaluationStartTime.

Publication logs include each row's actual delay and maximum delays among
playable rows. No fixed historical maximum is added to ScanTime: that would
double-count elapsed scanning time. Network failures can still increase the
age of snapshots collected before other candidates; the measured age is saved.

## Read-only historical replay

From the application directory:

    python3 tools/analyze_execution_timing.py

Uses candidate snapshots and cache only, latest admitted BellUp per ticker/day.
Previous completed M5 body and current M5 open are features; current final
close is only a future label. Legacy publication time is an approximation.
This measures five-minute forecast error against a current-open baseline;
it does not establish entry profitability. Exact past partial prices require
the new saved snapshots and cannot be recovered from completed OHLC alone.

User-run checks (the pure timing project has no broker/package dependencies;
CSV checks reference the application project but do not connect to TWS):

    dotnet run --project checks/ExecutionTimingChecks
    dotnet run --project checks/ExecutionCsvChecks
    dotnet run --project ../tests/CandidateCsvChecks
    dotnet run --project ../tests/BellUpBoostExitChecks
    dotnet run --project ../tests/EvaluationSelectionChecks

Builds, application runs and tests remain user-run under the project rules.

Initial read-only replay on September 21 found 24 eligible ticker-days with
explicit saved BellUp labels since September 1. Eighteen lack the adjacent
M5 pair, five have nonpositive previous bodies, and just one supports the
upward-body formula. This is insufficient evidence to activate the forecast.
The cohort excludes older snapshots lacking explicit BellUp labels.
