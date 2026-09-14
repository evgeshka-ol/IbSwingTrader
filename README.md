# IbSwingTrader

IbSwingTrader is a .NET command-line research and swing-trading assistant. It
connects to Interactive Brokers Trader Workstation (TWS), builds technical
features from market data, ranks candidate stocks, and evaluates historical
scan results. It is a research and decision-support tool; it does not place
live orders.

## Contents

- [High-Level Architecture](#high-level-architecture)
  - [TWS connection](#tws-connection)
  - [Historical data and aggregation](#historical-data-and-aggregation)
  - [Feature and pattern analysis](#feature-and-pattern-analysis)
- [CLI Commands](#cli-commands)
- [Outputs](#outputs)
- [The Scan-Evaluate-Analyze Loop](#the-scan-evaluate-analyze-loop)
- [Current Research State](#current-research-state)
- [Development Checks](#development-checks)

## High-Level Architecture

The application is organized around a dependency-injected application layer:

```text
CLI command
  -> application service (scanner, evaluator, dataset builder)
  -> domain models and rule engines
  -> infrastructure adapters (TWS, cache, CSV, logging, settings)
```

### TWS connection

`TwsConnection` wraps the Interactive Brokers C# API. It establishes an API
connection to the host, port, and client id from `agentsettings.json` (the
default is `127.0.0.1:7496`), starts the IB event reader, and converts TWS
callbacks into tasks used by the application. The same connection supports:

- IB market scanners and scanner parameters;
- contract details and market probes;
- historical OHLCV requests;
- fundamental report snapshots.

TWS or IB Gateway must be running with API access enabled before commands that
need broker data are started. The market timezone is normally
`America/New_York`.

Broker authentication is handled entirely by TWS or IB Gateway. The login
session, credentials, and any broker-side multi-factor authentication remain
on that side of the connection; IbSwingTrader only uses the local TWS API
socket. Consequently, the application does not contain broker credentials,
passwords, or other sensitive authentication data in its source code or
configuration files.

### Historical data and aggregation

`HistoricalDataService` is the single entry point for historical candles. It:

1. loads cached candles for a symbol and timeframe;
2. detects missing ranges at the edges of the requested interval;
3. requests only missing ranges from TWS, in bounded chunks with throttling and
   retries;
4. merges, de-duplicates, orders, and saves the result;
5. returns completed candles to feature and pattern analysis.

The scanner uses TWS-native timeframe streams, including D1, H4, M15, and M5.
H4 candles therefore follow the broker's canonical grid; the application does
not replace them with Yahoo Finance's session-aligned aggregation. Daily bars
are date-based and incomplete bars are excluded according to the market-time
completion rules.

The per-symbol cache is stored under `Data/cache/<SYMBOL>.json`. Each file
contains timeframe candles, coverage metadata, and a derived H4 pattern
snapshot. Cache files are an operational data store, not source code.

### Feature and pattern analysis

The feature engine calculates Bollinger Bands, RSI, MACD, moving-average
context, volume and related series. Candidate logic then applies the scanner
presets, wishlist filters, daily-family split, and pattern rules:

- `Runaway` is currently focused on confirmed `BellUp` on H4 or Daily;
- `Reversal` uses the reversal-hook family;
- everything else is retained as `Other` for evaluation and diagnostics.

BellUp timing uses completed candles and the positive-body boost rule. Current
trade-plan safeguards include lower-band-turn exhaustion detection, the
H4-BellUp plus flat-Daily-mid exhaustion filter, late-entry penalties, and an
exit target derived from one or two preceding qualifying boosts.

## CLI Commands

Run commands from the repository root with `dotnet run --project IbSwingTrader --
<command>`.

| Command | Purpose |
| --- | --- |
| `get-candidates` | Run configured IB scans, fetch required history, analyze/rank candidates, and write `Data/Tickers/candidates.csv`. |
| `evaluate-candidates` | Evaluate eligible historical candidate scans against subsequent M5/M15 market data and update evaluation outputs. Current-day scans are deferred until the next eligible evaluation cycle. |
| `normalize-reports` | Normalize and rebuild report/evaluation files, including derived fields and current pattern verdicts. |
| `build-research-dataset` | Build the research-oriented dataset used for historical analysis. |
| `build-dataset <trades.csv> <dataset.csv>` | Build a trade dataset from a supplied trade history. |
| `get-scanner-params` | Query and print scanner parameters available from TWS. |
| `download-fundamental-snapshot` | Download configured fundamental/company report snapshots. |
| `clean-up` | Apply configured cleanup rules to generated data and reports. |

The command dispatcher and usage text live in `IbSwingTrader/App/Program.cs`.
Settings are centralized in `IbSwingTrader/agentsettings.json`, including
paths, TWS connection details, market sessions, scanner presets, evaluation
policy, and retention rules.

## Outputs

The important generated files are under `Data/`:

- `Tickers/candidates.csv`: append/merge history of scanner candidates. It
  contains the candidate group, trade plan, ranking data, feature series, and
  `PatternVerdictReason` such as `BellUp confirmed on H4` or
  `BellUp confirmed on Daily` when the scanner identifies BellUp.
- `datasets/evaluation-dataset.csv`: row-oriented historical evaluation data,
  including `DetectedPipeline`, `DetectedPattern`, `PatternVerdict`,
  `PatternVerdictReason`, signed amplitude, extrema timestamps, entry/exit
  outcomes, and derived timing metrics.
- `Tickers/wishlist.csv`: merged scanner universe and wishlist state.
- `datasets/trade_dataset.csv`: optional trade-history dataset produced by
  `build-dataset`.
- `logs/`: application diagnostics and timing information.

Generated CSV/cache files are intentionally ignored by Git. The `docs/`
directory contains dated design notes for scanner and evaluator changes.

## The Scan-Evaluate-Analyze Loop

The project is developed as an empirical feedback loop:

1. **Scan.** Run `get-candidates`. The scanner queries TWS, refreshes cache
   edges, calculates features, classifies candidate families/patterns, builds
   an entry/exit/stop plan, ranks rows, and writes the current scan plus
   retained history.
2. **Wait for market data.** The scan timestamp is treated as the information
   boundary. Future candles are not available to scanner decisions.
3. **Evaluate.** Run `evaluate-candidates` on a later trading day. The
   evaluator selects eligible scan dates, loads post-scan M5/M15 data, tracks
   entry, target, stop, maximum/minimum excursions, extremum order, and open or
   closed outcomes.
4. **Analyze.** Inspect the evaluation CSV, charts, logs, and dated research
   notes. Compare ranking, timing, pattern verdicts, signed amplitude, and
   trade-plan behavior with the observed price path.
5. **Change and repeat.** Turn validated observations into a narrow rule or
   threshold change, add focused checks where practical, rebuild, and run new
   scans. Historical rows are evidence for the next iteration, not proof that
   a rule will generalize.

## Current Research State

The current iteration is concentrated on making `Runaway` a high-precision
BellUp list rather than a broad momentum list. The main active questions are:

- detecting BellUp at the correct completed-candle moment on H4 and Daily;
- rejecting an exhausted H4 BellUp when the lower band has turned upward and
  the Daily Bollinger mid line is flat;
- avoiding late entries near a prior boost or candle high;
- setting a realistic exit from the nearest qualifying boost (one boost is
  accepted; two are averaged);
- preserving broker-native H4 timing and treating Yahoo chart aggregation as
  visual reference only;
- measuring whether these changes improve the first-ranked candidate after
  the next-day evaluation.

The project is currently in the **scan -> next-day evaluation -> manual
analysis** phase. Recent changes are being validated with consecutive scans and
their following evaluation reports. The immediate success criterion is not a
large candidate count: it is recurring high-quality, correctly timed BellUp
entries near the top of `Runaway`, while exhausted or ambiguous setups are
removed or moved to `Other`.

## Development Checks

Focused console check projects are available under `tests/` for scanner timing,
evaluation selection and amplitude direction, and BellUp exit/exhaustion
rules. The main application is built with the normal .NET SDK toolchain; TWS
integration checks require a reachable TWS/IB Gateway instance.
