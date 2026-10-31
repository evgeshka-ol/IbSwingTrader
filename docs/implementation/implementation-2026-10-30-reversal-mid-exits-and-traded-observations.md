# ReversalHook: Daily-mid/10% exit and dependable observations

Implemented2026-10-30 after the user's approval of the
[report audit](../investigations/2026-10/investigation-2026-10-30-exits-readiness-and-data.md).
The user added: exit at the mean when nearer than10%; otherwise retain10%.
Daily EMA20 is used consistently with that report for every admitted D1/H4 hook.

## Resulting behavior

- Entry remains the exact fresh scan reference. Exit is
  `floor(min(native completed Daily EMA20, Entry * 1.10) * 100) / 100`.
  The published target is fixed, not a trailing recalculation. Old5%/8%
  profiles/floors/buffering and BellUp exits cannot override it. Existing stop
  risk controls are rebuilt from the entry and new target. No general1.5% floor
  is added. `reversal-daily-mid`/`reversal-10pct-cap` identify the policy.
- Native Daily history must have20 bars and the expected last closed trading
  date. Missing/stale/reached mean or no positive cent-sized room rejects the
  plan. Reasons retain full-precision ExitDailyMid, ExitTarget and ExitRule.
- A completed Daily close reaching its own EMA after seed confirmation consumes
  the first-return stage, including the seed's own closing proof. High-only
  proof requires an entire Daily bar starting after confirmation; no earlier
  seed-day wick is assumed to follow the trigger. Fresh price at/above mean
  also rejects. Existing post-BellUp/upper completion remains. New seeds are
  independent. This is a late-entry guard, not a claim that price cannot rise.
- Shared traded-observation policy requires positive volume and valid OHLC.
  Zero-volume M5/M1 cannot supply fresh quotes, body forecasts or execution
  touches. BellUp historical ScanPrice fallback is removed. One bounded stale
  plan retry follows sequential scanner work; the writer checks freshness
  before sorting/saving (M5<=5min; live<=configured age). Expiry becomes Other.
- BellUp spike-readiness checks only the latest completed transition, retaining
  thresholds. An old spike no longer vetoes unrelated later readiness. Saved
  reasons distinguish spike, local Direction/Collapse and H4 contradiction.
  RecentBoost remains; IOVA's later collapse is a useful negative control.
- Evaluation version8 checks completed M5 only, repairs missing configured
  regular slots with one bounded direct request per affected session, then
  evaluates only the covered prefix if gaps persist. No entry yields
  IncompleteHistory; confirmed unresolved entry remains Open. NoEntry and
  unentered IncompleteHistory playable snapshots retry within the forward
  window, including older scans. Other/zero plans and closed Win/Loss are not
  reopened by this policy. Summary logs count IncompleteHistory separately.
- Later ambiguous M5 uses its exact five M1 despite legacy timeframe metadata.
  Entry need not touch again. Same-minute ambiguity or missing M1 conservatively
  prefers stop without candle-colour/proximity guessing. The first-entry suffix
  replay remains separate. Feedback excludes zero-volume progress; historical
  saved Win continues FinalFromOutcome. Dataset cache metrics for version8
  respect the saved covered horizon and positive trade volume. Price rebasing
  no longer downgrades8 to7.

## Evidence and verification status

Frozen native D1 prefixes, without future entry outcomes:

- SOFI through October26:166 bars; Daily EMA15.2328208771 versus fresh14.43,
  target15.23 (about5.54%). No completed post-seed close reaches mean. Its
  existing anticipatory support remains separately checked.
- AMD through October28:173 bars; close578.44 versus EMA562.0966874945 on the
  seed-confirmation day. This proves first-return completion before October29
  regardless of the later pullback/outcome. Fixture records cache SHA256 and
  cutoff; future cached candles are excluded from its contents.

Offline Python inspected these prefixes, XML/project links, Markdown links
and focused diffs. Codex did **not** run the application, broker requests,
builds or C# tests. No successful C# verification is claimed.

Prepared checks cover exact mid/cap/cent boundaries, fractional fresh quotes,
legacy profile override, invalid mean, publication expiry, frozen AMD/SOFI,
seed-day wick ordering/new seed/future exclusion, old/latest spike, positive
volume touches, regular gap repair/prefix censoring, zero-volume windows,
holidays/early close, M1 ambiguity, version preservation and pending windows.

User-run verification:

```powershell
dotnet build IbSwingTrader/IbSwingTrader.csproj
dotnet run --project tests/ReversalEntryChecks
dotnet run --project tests/ReversalRecoveryChecks
dotnet run --project tests/ExecutionTimingChecks
dotnet run --project tests/EvaluationExecutionChecks
dotnet run --project tests/EvaluationSelectionChecks
dotnet run --project tests/BellPatternChecks
dotnet run --project tests/EvaluationConfidenceChecks
dotnet run --project tests/CandidateCsvChecks
```

## Scope and subsequent measurement

Historical candidate plans, CSV/cache market data and resolved outcomes were
not rewritten. Reevaluate old results only through their configured policy;
new scanner targets must not be retroactively substituted into saved plans.
The frozen fixtures are source snapshots, not a full broker-backed replay.

BellUp's boost-body exit/minimum/reduction mechanism remains. Its stage/remaining
room and a separate W1-after-pause continuation/reentry scenario require further
prefix-based research. CDNA can still fail H4 contradiction after stale-spike
repair. No Weekly-only admission, blanket removal of RecentBoost, new weekly
holding plan, universal extra target divisor or confidence refit was added.

Compare later entered Win/Loss/Open, winning profit and rank1 outcomes under
these policies. More admitted entries or a lower target alone does not establish
better performance; report incomplete coverage and confidence denominators.

Current contracts: [scanner entry/exit](../project-skills/ibswingtrader-scanner/references/REVERSAL_SCAN_PRICE_ENTRY.md),
[completion](../project-skills/ibswingtrader-scanner/references/REVERSAL_IMPULSE_COMPLETION.md),
[evaluation](../project-skills/ibswingtrader-evaluation/references/EVALUATION_RULES.md).
