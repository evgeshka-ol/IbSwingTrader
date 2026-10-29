# ReversalHook entry at the fresh scan price

Current execution policy, adopted2026-10-28 at the user's direction after the
29-plan audit found27 recorded NoEntry and a median entry discount of7%.
See the [dated audit](../../../investigations/2026-10/investigation-2026-10-28-reversal-entry-policy.md)
for original results and the separate frozen-NoEntry evaluation issue.

## Publication contract

After recognition, phase, origin/completion, higher-timeframe support and
fresh-price validation pass, a playable ReversalHook uses:

```text
EntryPrice = TradePlan.LiveReferencePrice = candidate CSV ScanPrice
```

The reference is the refreshed M5 snapshot or the existing acceptable IB live
fallback. It is not the earlier indicator close, last M15 close, a future candle
open or BellUp's M5 body projection. Existing price source/time/observed fields
retain provenance, and existing quote/structure guards still apply.

`CandidateFinder.BuildTradePlan(... reversalPublication:true)` skips the legacy
entry-adjustment block and calls TradeBuilder's direct scan-price entry path.
No DeepPullback/profile discount, Bollinger floor, triangle entry adjustment
or M15 pattern forecast may lower or raise that entry. Explicit discount/floor
arguments are cleared at the builder call. TradeBuilder rejects a nonpositive
reference for direct ReversalHook entry, instead of falling back to history.
Publication also verifies exact entry/reference equality and sets the admitted
candidate's NeedsDeeperEntry to false.

Targets and stops are rebuilt by TradeBuilder using the new entry and existing
exit/risk-profile parameters. This is not replacement of an old EntryPrice
while retaining old absolute target/stop levels. Exit must exceed entry, stop
must be positive and below entry. Existing risk caps, stop-limit ordering and
target calculation remain in force. Exit-profile classification is distinct
from the retired pullback entry policy for admitted hooks.

The rule applies to every admitted origin and H4/D1 timeframe, including
the user-authorized2026-10-29 [anticipatory D1 support](WEEKLY_MID_SLOPE.md)
that can admit selected local Stalled episodes. Entry policy itself does not
authorize a phase: unsupported Stalled, Completed and stale-price diagnostics
remain rejected. BellUp retains its existing publication entry behavior. Historical saved plans/outcomes are
not rewritten; refreshing evaluation feedback does not replay them.

ScanPrice is a reference for the published plan, not a guaranteed broker fill.
Evaluation continues to use post-publication market candles and its existing
entry/target/stop ordering rules. The separate NoEntry reevaluation policy is
not changed by this execution-policy update.

## Prepared verification

ReversalEntryChecks uses the real TradeBuilder and shipped settings with
synthetic H4/M15 histories to verify exact AAL/NVDA/INTC/AAOI and fractional
quote references, bypass of hostile legacy discounts/floors and differing
historical prices, rebuilt target/stop caps, missing-reference rejection,
absence of M15 and preservation of explicit legacy non-publication behavior.
These are plan-construction checks, not replayed historical admissions or Wins.

User-run commands:

```powershell
dotnet build IbSwingTrader/IbSwingTrader.csproj
dotnet run --project tests/ReversalEntryChecks
dotnet run --project tests/ExecutionTimingChecks
```

Validate subsequent entered Win/Loss/Open, profit and rank1 results under the
new policy. More entries alone do not establish improved performance.
