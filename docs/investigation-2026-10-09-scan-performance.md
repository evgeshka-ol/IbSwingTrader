# Scan slowdown and guards — 2026-10-09

## Evidence before changes

log-20261009_0334.log: get-candidates 3:55:40; Ranking/Rebuild 2:06:13;
XOM in Ranking/Rebuild 1:37:01. The XOM M5 bridge started at 2026-06-24
20:00 and ended at 2026-10-09 05:21:39. Across the run, 200 XOM requests
timed out, while 1496 completed historical requests for other tickers
accounted for about 8.2 minutes of logged request waiting.

The preceding day reproduced the issue: total 4:12:24, XOM Ranking/Rebuild
1:36:04 and 196 XOM request timeouts. The bridge had no age cap or sustained
failure stop. Missing H4 history was effectively expanded into months of M5.

Removing the single XOM operation alone would leave about 2:19 on October 9.
The run handled 151 unique tickers through 454 ticker operations, 537 history
load counters and 199 bridge diagnoses. Repeated history preparation and
feature calculations are additional targets, not measured speed gains yet.

## Implemented

1. Finder.ReversalM5BridgeMaxCalendarDays=7. Stale H4 boundaries skip M5 and
   keep the setup non-playable. The code does not silently truncate a stale
   base and claim continuity has been restored.
2. Finder.HistoricalMaxConsecutiveEmptyChunks=3, explicitly passed by scanner
   range callers. Count expected trading periods after existing request retries;
   ignore off-session empty periods and reset on a nonempty chunk. Abort the
   range request at the limit. A provider empty result can mean timeout or
   genuinely absent data; the API does not distinguish these results.
   Other historical-service consumers keep previous behavior unless opting in.
3. Prepared H4/D1, contract and signal snapshot reuse by ticker within a run.
   Reuse expires by the next possible H4 close, at latest the next four-hour
   boundary. Preset-specific universe filtering/scoring remains separate.
4. Memoize feature prefixes, completed-series views and recent indicator
   series. Invalidate when publication refresh mutates input candles.
5. Bridge result reuse within the same M5 bucket, including no recent quote.
   Failed H4 preparation or exceptional bridge failure suppresses duplicate
   attempts in the same run. All such state resets for the next scan.

Final publication fresh-price requests remain independent. Old data and
missing price do not authorize a trade. Existing disk caches are not erased
when a load aborts. The settings and cache/abort events are documented in
project-skills/ibswingtrader-scanner/references/SETTINGS_MAP.md.

## Validation status

JSON parsing and targeted git diff --check passed. Application, broker
scans, builds and tests were not run, per repository responsibility rules.
Actual elapsed time and cache behavior require the next user-run scan.
Compare total time, slowest ticker operations, In-run reuse summary,
prepared history reuse messages and historical empty-chunk streaks.
No promise of returning to 40 minutes is made before that measurement.
