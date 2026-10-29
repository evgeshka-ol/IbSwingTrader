# ReversalHook admission, deep entries and frozen NoEntry evaluations

Saved-data/source audit,2026-10-28. The user reports several days of admitted
ReversalHook plans failing even to enter. This note diagnoses existing behavior;
it does not change the live entry or reevaluation policy.

## Inputs and reproducibility

- [Offline audit script](../../../tools/analyze_reversal_entry_audit.py).
- [Frozen summary, selected29 rows, report hash and later-touch cache hashes](../../artifacts/analysis-2026-10-28-reversal-entry-audit.json).
- Current `Data/datasets/evaluation-dataset.csv`, exact candidate snapshots
  joined on ticker/ScanTime/preset, native cached M5 and October26 scanner log.

The report's latest scan is October26 18:28:38 ET; latest EvaluatedAt across
the report is October28 04:58:37. Report filters mean these counts describe
saved report rows, not a certified census of all historical scans. Repeated
scans/tickers and unequal horizons are not independent observations.

## Observed participation

Since October19,29 playable ReversalHook rows across seven scans show27
NoEntry and two entered Losses, no recorded Win. All seven scan-local rank1
rows are NoEntry. October26 evening's12 admitted plans all remain NoEntry.

| Scan date/time ET | NoEntry | Entered Loss |
| --- | ---: | ---: |
| October19 04:50:32 | 2 | 0 |
| October21 17:23:56 | 6 | 2 |
| October22 04:14:03 | 1 | 0 |
| October22 17:19:12 | 2 | 0 |
| October23 04:26:32 | 1 | 0 |
| October23 17:19:46 | 3 | 0 |
| October26 18:28:38 | 12 | 0 |

The two entered records are DDOG and CIFR from October21. These are saved
outcomes, not a claim that the other27 failed to enter over a completed10-day
horizon: frozen early NoEntry is a separate problem below.

## Main mismatch: active recovery admission, deep-pullback entry construction

Median `(1-EntryPrice/ScanPrice)*100` is7.0028%, range0.62–8.92%.
All27 NoEntry rows have their observed minimum above EntryPrice. NoEntry
diagnostics contain20 TargetBeforeEntry and seven EntryTooDeep. The target
label is not evidence of an executable win: some targets already lie below
the price at publication because the entire plan is based on a deep entry.

October26 examples, using each row's saved observation horizon:

| Ticker | ScanPrice | EntryPrice | Observed minimum | ExitPrice |
| --- | ---: | ---: | ---: | ---: |
| AAL | 12.21 | 11.36 | 12.15 | 12.26 |
| NVDA | 216.40 | 201.33 | 214.40 | 213.41 |
| INTC | 91.37 | 84.80 | 89.16 | 85.81 |
| AAOI | 72.91 | 67.71 | 70.18 | 73.06 |

Source chain:

1. Final `RefreshReversalForPublication` recognizes/adopts an Active or
   MidCrossed episode and passes current higher-timeframe support, then calls
   `BuildTradePlan(... reversalPublication:true)`.
2. In CandidateFinder, the separate legacy `isReversalRecovery` and
   `isReversalNearTermEntry` heuristics determine the2%/2.5% entry caps. Being
   a freshly admitted ReversalHook does not automatically activate either.
3. `ResolveSeriesEntryProfileDiscountOverridePct` can assign DeepPullback
   with configured `DeepPullbackDiscountPct=0.07`. No universal admitted-hook
   cap follows: the existing cap is conditional on the older heuristics.
4. TradeBuilder uses M15 pattern entry when found, or discounts the last M15
   close by the profile. Therefore the final distance from fresh ScanPrice
   may differ slightly from exactly7%. Fresh publication pricing does not
   imply a fresh-price entry; it supplies the reference/provenance.

The October26 log confirms final publication plans using DeepPullback7%
for AAL18:15:21, INTC18:21:01, NVDA18:23:58, CMG18:25:30 and ATI18:28:15;
AAOI18:26:08 uses weak deep-pullback7%. No hypothetical indicator re-fit is
needed to explain why their entry levels were never touched in saved horizons.

This behavior asks for a second large drop after recognizing an upward recovery.
It can leave the candidates that keep rising unfilled, while admitting those
that deteriorate enough to reach the low entry. The latter adverse-selection
mechanism is a hypothesis supported by the placement, not validated from only
two entered losses.

Eleven of29 targets are at or below ScanPrice, including six of12 on October26.
INTC's target85.81 is6.09% below scan91.37 and its saved planned entry-relative
profit is1.19%. A shallower entry must rebuild target and stop and validate
remaining pattern room; moving EntryPrice alone would produce invalid plans.

## Independent evaluation mismatch: early NoEntry becomes final

Configuration specifies ForwardEvaluationDays10, but AllowedOutcomesToEvaluate
contains only Open. EvaluateCandidatesCommand skips saved NoEntry in the
recent-date path, and its older-row catch-up explicitly selects Open. Thus a
morning evaluation may freeze NoEntry before the next regular session even
though its nominal forward window has barely begun.

For example, October21 evening CLSK and IREN were evaluated at October22
04:27/04:31 and remain NoEntry. Cache contains later M5 ranges crossing their
saved entry levels, within the nominal10-day window:

| Plan | Entry | Later M5 start ET | M5 low / high |
| --- | ---: | --- | ---: |
| CLSK October21 17:23:56 | 8.20 | October23 04:00 | 8.15 / 8.32 |
| IREN October21 17:23:56 | 26.19 | October23 04:00 | 25.65 / 26.19 |
| T October22 17:19:12 | 21.13 | October23 16:10 | 20.95 / 21.22 |
| CRGY October19 04:50:32 | 13.52 | October22 09:30 | 13.51 / 13.71 |

These are later cached price-level touches, not broker fills or replayed
Win/Loss. M1 ordering, gap execution, target/stop ordering, plan expiry and
cache coverage still matter. Cache absence cannot prove no later entry.
The report count27 NoEntry therefore combines real observed no-touch with
incomplete reevaluation; it is not the final10-day participation rate.

## Rejected hooks are a separate cohort

The same report has1,438 Other/ReversalHook rows since October19. Leading
explicit reasons: Stalled644, Completed152, missing acceptable fresh quote129,
W1 Collapse/Down83, plain D1 Neutral/Down77, W1 Neutral/Down54. Other historical
reason variants retain their exact strings in the artifact. These diagnostics
are not admitted plans and must not be pooled into the29-plan fill-rate count.
The current NN weekly-support exception cannot retroactively change old
admissions or solve the7% entry construction for already admitted H4 hooks.

## Concrete next changes to evaluate

1. Make the waiting-for-entry evaluation horizon explicit and keep playable
   NoEntry eligible for reevaluation until that horizon expires. Older waiting
   rows need catch-up, not just adding NoEntry to a recent-date configuration
   list. Zero-plan Other should not generate repeated broker replay work.
2. Give a freshly confirmed ReversalHook its own execution policy tied to the
   fresh reference and remaining pattern target room, with recomputed stop and
   target. Separate a deliberate deep-pullback strategy from immediate recovery
   admission. Do not globally force market entries or keep old targets.
3. Compare fill participation, resolved Win/profit, Open/unfilled horizons and
   rank1 under the same policy. Removing a7% wait does not prove profitable
   trades, and a large future maximum is not a Win.

Only read-only/offline analysis was run for this audit. No application, broker
scan, C# build, test, saved outcome replay or trading-policy edit was performed.
