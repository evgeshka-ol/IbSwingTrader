# Bollinger EMA and fresh H4 reversal momentum

Current implementation, 2026-10-17. User-directed changes following the VSTM
Stalled-reclaim case. Application builds, source checks and broker runs remain
user-run; the checks listed below have been prepared but not executed by Codex.

## Bollinger model

Model identifier: `ema20-sma-seed20-popstd20-v1`.

For H4, native D1 and Weekly Bollinger curves:

- Center: EMA20 of closes, alpha=2/21. The first valid value is the SMA of
  the first 20 closes; subsequent values retain the entire available prefix.
  Do not restart EMA in each recent 20/30-bar export window.
- Population sigma: last 20 closes around their arithmetic mean, divisor20.
- Upper/lower: EMA20 +/- 2*sigma. Width percentage uses EMA20 as denominator.
- Bollinger-mid distance percentages use that same EMA20. This keeps
  mid*(1+distance/100) equal to its reference close, including ResolveScanPrice.
- Plain MA fields (for example DailyMaSignedDistancePct and H4's SMA50
  context) retain their former SMA definitions. They are not Bollinger mids.

This is the explicit EMA-center variant selected as a working assumption
while awaiting any more specific TWS convention. It is not a claim of exact
TWS indicator parity: IB's public overview lists SMA/WMA/EMA centers, but
exact seed/deviation conventions have not been matched against TWS values.
[IB Bollinger overview](https://www.interactivebrokers.com/campus/glossary-terms/bollinger-bands/).

The common FeatureEngine supplies scan features, native watch-history replay,
partial diagnostics, cache snapshots, research and trade datasets. Enrollment
is now measured at least5% below the completed Daily EMA Bollinger mid.
No thresholds or ranking weights were retuned to compensate for the model
change. Faster response is not proof of improved Win/profit performance.

## Saved data and model migration

New scanner ScanInfo and fresh research/trade/cache snapshot records carry
BollingerModel. Evaluation preserves the candidate model into its result and
dataset; it does not relabel old candidates with the current formula. Blank
historical metadata means legacy/unspecified, normally the earlier SMA model.
Historical CSV rows, raw candles, frozen model coefficients and dated study
results are not rewritten by this source change. Newly generated indicator
snapshots use EMA. Retain model cohorts when analyzing scores and confidence.
The existing Win v1 model has not been recalibrated on EMA-derived plans.

Watch lifecycle states record BollingerModel. A change resets inspected D1/H4
cursors for a cache-only replay under the new geometry. Consumed hook/cycle
memory survives; an old SMA rearm is not silently trusted for the EMA zone.
Active cycle-start timestamps survive, so replayed old seeds do not consume
an already newer drop cycle. No direct broker scan of all retired symbols
is introduced by this migration.

## Fresh H4 support for a recognized episode

The previous live-reclaim rule could only revive a mid-bend episode when the
last CLOSED histogram of that same timeframe was at least its bend value.
For a Daily hook, yesterday's weak Daily MACD could therefore veto today's
already observable H4 improvement.

The live-reclaim branch now accepts either that existing momentum condition
or improving provisional current H4 histogram. It still requires:

- a recognized completed-bar mid-bend seed (D1 or H4);
- fresh price above the episode timeframe's current mid and pre-bend close;
- the pre-bend close below its mid;
- the existing upper-target-completion and price-invalidation checks.

Historical strict-hook seeds without a discrete bend retain their existing
local continuation checks. The new momentum observation does not itself
establish any seed. Preparing/unrecognized patterns do not become admitted
from MACD alone. Ordinary publication still checks recognition, active phase,
BellUp precedence, higher-timeframe support, fresh price and plan validity.
This is not an unconditional admission of every Stalled setup.

## Close-only provisional H4, not a confirmed forming candle

MACD requires closes, not OHLC. Reuse the completed native H4 close prefix
and append the current fresh quote price to calculate a single provisional
MACD point with the SAME MACD primitive as the feature engine. No candle is
fabricated, no future completed H4 value is read and no extra request is made.
The observation applies to the current native04/08/12/16 interval only.
Require the last expected closed native H4 from the local trading schedule,
chronological completed inputs and a price timestamp inside the current H4,
no older than10 minutes. Future price/receipt times beyond2 seconds fail.
The existing M5/live-quote acceptance checks remain in place before this
additional guard. An old-session quote or missing current H4 base cannot
provide support.

Compare provisional histogram strictly with the previous CLOSED H4 histogram.
Do not require positivity: a negative value moving toward zero is improvement.
This is a conditionally user-authorized readiness change, not an independently
validated ranking feature, trading guarantee or claim of two independent
signals (price and its derived MACD are related).

Full partial-H4 OHLC diagnostics still require complete returned-M5 coverage
and remain diagnostic-only. Incomplete OHLC coverage does not prevent a
close-only MACD calculation when its completed prefix and current quote are
valid. The two observations have different data requirements and labels.

PatternVerdictReason records RecoveryMomentum Source=FreshPriceProvisionalH4,
Improving, PartialStart, decision time, previous closed H4 time, price source/
time/receipt, exact price, previous histogram, provisional MACD line/signal/
histogram. Unavailable support has an explicit reason. It is rederived during
publication refresh using that publication's quote and refreshed closed bars.

## VSTM and the effect of changing two mechanisms

On the ORIGINAL SMA snapshot, VSTM had a D1 bend on October7, weak closed
D1 histogram -0.062339423 versus bend -0.022980832, and fresh8.11 above
mid7.9165/pre-bend7.75. Last closed H4 histogram was -0.053476806;
close-only provisional H4 at8.11 is about -0.033390430. The new live momentum
branch addresses that phase veto without assuming a future completed H4.

EMA also changes seed geometry. Cache-only Decimal reconstruction finds no
October7 D1 mid-bend seed in the final six bars under EMA, but finds an H4
mid-bend seed on October14 04:00 (pre-bend close7.53). Latest closed H4 EMA
is about8.02554, below fresh8.11. This is not a full C# classification or
publication replay. Do not promise that the new application emits the same
D1 label or passes all other readiness checks solely from the old SMA case.
The [original VSTM investigation](../../../investigations/2026-10/investigation-2026-10-17-vstm-stalled-reclaim.md)
retains its original model and preliminary-plan touch check.

## User-run verification

```text
dotnet run --project tests/BollingerEmaChecks
dotnet run --project tests/ReversalRecoveryChecks
dotnet run --project tests/ReversalWatchListChecks
dotnet run --project tests/ExecutionCsvChecks
```

EMA checks use hand-calculated seed/step/variance examples, price-reconstruction
identities, all available timeframe scales, old plain-MA semantics, prefix-only
inputs and native watch-history parity. Recovery checks cover the original
VSTM-like recognized seed, negative and stale/misaligned-price controls, missing
H4 history, no fabricated seed, target completion and unchanged input candles.
Watch checks include model-cursor migration without forgetting consumed cycles;
CSV checks cover model preservation and legacy blanks. Actual Win/profit and
confidence calibration require subsequent user-run scans with model cohorts.
