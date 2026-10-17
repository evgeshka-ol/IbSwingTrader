# IbSwingTrader

IbSwingTrader is a .NET 10 command-line research and swing-trading assistant. It connects to Interactive Brokers Trader Workstation (TWS), builds technical features from market data, ranks candidate stocks, and evaluates historical scan results. It is a research and decision-support tool; it does not place live orders.

## Contents

- [High-Level Architecture](#high-level-architecture)
  - [TWS connection](#tws-connection)
  - [Historical data and aggregation](#historical-data-and-aggregation)
  - [Feature and pattern analysis](#feature-and-pattern-analysis)
- [AI-Assisted R&D & The Battle of Agents](#ai-assisted-rd--the-battle-of-agents)
- [CLI Commands](#cli-commands)
- [Outputs](#outputs)
- [The Scan-Evaluate-Analyze Loop](#the-scan-evaluate-analyze-loop)
- [Current Research State](#current-research-state)
- [Documentation map](docs/README.md)
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

`TwsConnection` wraps the Interactive Brokers C# API. It establishes an API connection to the host, port, and client id from `agentsettings.json` (the default is `127.0.0.1:7496`), starts the IB event reader, and converts TWS callbacks into tasks used by the application. The same connection supports:

- IB market scanners and scanner parameters;
- contract details and market probes;
- historical OHLCV requests;
- fundamental report snapshots.

TWS or IB Gateway must be running with API access enabled before commands that need broker data are started. The market timezone is normally `America/New_York`.

Broker authentication is handled entirely by TWS or IB Gateway. The login session, credentials, and any broker-side multi-factor authentication remain on that side of the connection; IbSwingTrader only uses the local TWS API socket. Consequently, the application does not contain broker credentials, passwords, or other sensitive authentication data in its source code or configuration files.

### Historical data and aggregation

`HistoricalDataService` is the single entry point for historical candles. It:

1. loads cached candles for a symbol and timeframe;
2. detects missing ranges at the edges of the requested interval;
3. requests only missing ranges from TWS, in bounded chunks with throttling and retries;
4. merges, de-duplicates, orders, and saves the result;
5. returns completed candles to feature and pattern analysis.

The scanner uses TWS-native timeframe streams, including D1, H4, M15, and M5. H4 candles therefore follow the broker's canonical grid; the application does not replace them with Yahoo Finance's session-aligned aggregation. Daily bars are date-based and incomplete bars are excluded according to the market-time completion rules.

The per-symbol cache is stored under `Data/cache/<SYMBOL>.json`. Each file contains timeframe candles, coverage metadata, and a derived H4 pattern snapshot. Cache files are an operational data store, not source code.

### Feature and pattern analysis

The feature engine calculates EMA20-centered Bollinger Bands (±2 population
standard deviations over 20 closes), RSI, MACD and supporting
series. Current candidate groups are `BellUp`, `ReversalHook` and `Other`.
BellUp has precedence; recognized setups rejected by readiness remain Other.
Fresh H4 MACD improvement can support a recognized hook's live mid reclaim.
New snapshots carry `BollingerModel` to distinguish the EMA migration from
legacy SMA data. ReversalHook recognizes recent completed D1/H4 episodes independently of
the former Daily-mid family split and can remain active after crossing mid.

BellUp timing compares the latest green completed candle's range with the
median range of five preceding candles (default multiplier 2). Exit boost
selection remains body-based. Final BellUp profit targets of at least 10%
are reduced by half by default, preserving the existing 1.5% floor.
Exact pattern, freshness and execution contracts live in the
[project skills](docs/README.md#current-project-rules).

## AI-Assisted R&D & The Battle of Agents

This platform serves as an empirical playground for advanced **AI-Assisted Development and Dual-Agent Orchestration**. The complex domain logic — specifically the mathematical formalization of trading patterns and quantitative filters — evolved through three distinct phases of AI integration:

- **ChatGPT (Early Prototyping):** The project originated with fundamental architectural layouts and initial logic spikes generated via standard ChatGPT prompt engineering and manual copy-paste integration.
- **OpenAI-Powered Autonomous Code Agent (Codex):** As the platform scaled, Codex was introduced to replace manual workflows, acting as the primary execution engine. Following the release of the **Astra (GPT-6.0)** models, Codex re-entered the loop with reasoning capabilities on par with top-tier Anthropic models. Due to its superior workflow ergonomics and seamless integration, Codex currently holds the analytical edge in the platform's ongoing development.
- **Anthropic Claude:** Utilized as the high-level Architectural and Quantitative Critic. Claude challenged edge cases in the mathematical boundaries of the `BellUp` positive-body boost rule and forced the implementation of rigid backtesting safeguards (such as the H4-BellUp / flat-Daily-mid exhaustion filters).

This multi-generation, dual-agent "battle" eliminated mathematical hallucinations in pattern detection and allowed for rapid prototyping of the **Scan-Evaluate-Analyze** loop without compromising software engineering rigor.

## CLI Commands

Run commands from the repository root with `dotnet run --project IbSwingTrader -- <command>`.

| Command                                    | Purpose                                                                                                                                                                                    |
| ------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `get-candidates`                           | Run configured IB scans, fetch required history, analyze/rank candidates, and write `Data/Tickers/candidates.csv`.                                                                         |
| `evaluate-candidates`                      | Evaluate eligible historical candidate scans against subsequent M5/M15 market data and update evaluation outputs. Current-day scans are deferred until the next eligible evaluation cycle. |
| `normalize-reports`                        | Normalize and rebuild report/evaluation files, including derived fields and current pattern verdicts.                                                                                      |
| `build-research-dataset`                   | Build the research-oriented dataset used for historical analysis.                                                                                                                          |
| `build-dataset <trades.csv> <dataset.csv>` | Build a trade dataset from a supplied trade history.                                                                                                                                       |
| `get-scanner-params`                       | Query and print scanner parameters available from TWS.                                                                                                                                     |
| `download-fundamental-snapshot`            | Download configured fundamental/company report snapshots.                                                                                                                                  |
| `clean-up`                                 | Apply configured cleanup rules to generated data and reports.                                                                                                                              |

The command dispatcher and usage text live in `IbSwingTrader/App/Program.cs`. Settings are centralized in `IbSwingTrader/agentsettings.json`, including paths, TWS connection details, market sessions, scanner presets, evaluation policy, and retention rules.

## Outputs

The important generated files are under `Data/`:

- `Tickers/candidates.csv`: append/merge history of scanner candidates. It contains the candidate group, trade plan, ranking data, feature series, and `PatternVerdictReason` such as `BellUp confirmed on H4` or `BellUp confirmed on Daily` when the scanner identifies BellUp.
- `datasets/evaluation-dataset.csv`: row-oriented historical evaluation data, including `DetectedPipeline`, `DetectedPattern`, `PatternVerdict`, `PatternVerdictReason`, signed amplitude, extrema timestamps, entry/exit outcomes, and derived timing metrics.
- `Tickers/wishlist.csv`: active meaningful-drop cycles awaiting ReversalHook;
  `Tickers/wishlist.lifecycle.json` retains consumed-hook and rearm state.
- `datasets/trade_dataset.csv`: optional trade-history dataset produced by `build-dataset`.
- `logs/`: application diagnostics and timing information.

Generated CSV/cache files are intentionally ignored by Git. Start with the
[documentation map](docs/README.md) for current rules, operating guides,
dated investigations, implementation records and reproducible artifacts.

## The Scan-Evaluate-Analyze Loop

The project is developed as an empirical feedback loop:

1. **Scan.** Run `get-candidates`. The scanner queries TWS, refreshes cache edges, calculates features, classifies candidate groups/patterns, builds an entry/exit/stop plan, ranks rows, and writes the current scan plus retained history.
2. **Wait for market data.** The scan timestamp is treated as the information boundary. Future candles are not available to scanner decisions.
3. **Evaluate.** Run `evaluate-candidates` on a later trading day. The evaluator selects eligible scan dates, loads post-scan M5/M15 data, tracks entry, target, stop, maximum/minimum excursions, extremum order, and open or closed outcomes.
4. **Analyze.** Inspect the evaluation CSV, charts, logs, and dated research notes. Compare entered-plan wins, winning profit percentages and saved confidence with actual outcomes. Use timing, pattern verdicts and amplitude to explain failures.
5. **Change and repeat.** Turn validated observations into a narrow rule or threshold change; the user runs builds, tests and new scans. Historical rows are evidence for the next iteration, not proof that a rule will generalize.

## Current Research State

The [current success criteria](docs/guides/success-criteria.md), confirmed by
the user on 2026-10-17, prioritize:

1. Winners among candidates that entered under their saved plans.
2. Profit percentages of those winners.
3. Agreement of `DiagnosticsEstimatedHitRatePct` with actual results.

The minimum goal is a dependable winner at rank 1, with profit of at least
1.5% sufficient. This is the requested goal, not an achieved guarantee.
Inspect the original leading playable row and its ranking scope; report
entered Win/Loss/Open and NoEntry separately. The current confidence model
estimates Win conditional on entry and resolution; it is experimental.

Amplitude and next-day `research_top_gainers.csv` coverage remain supporting
diagnostics for recognition, recall, timing and ranking. They do not replace
actual Win/profit results or make amplitude below 10% an automatic failure.
Dated studies retain their original assumptions and results.

## Development Checks

Focused console check projects are available under `tests/` for scanner timing, evaluation selection and amplitude direction, and BellUp exit/exhaustion rules. The main application is built with the normal .NET SDK toolchain; TWS integration checks require a reachable TWS/IB Gateway instance.

Execution checks also live under `tests/`: `ExecutionTimingChecks` covers M5
price freshness and entry projection; `ExecutionCsvChecks` covers timestamp
and price precision through CSV round trips.
