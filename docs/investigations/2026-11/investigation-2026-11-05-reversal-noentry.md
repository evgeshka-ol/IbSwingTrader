# November 5 report: published reversals without evaluated entries

Reviewed 2026-11-05 using saved candidate/evaluation CSV, cache and evaluator log.
No application, broker requests, builds or tests were run. No plans or outcomes
were changed. This is an investigation, not a new execution policy.

## Cohort and results

The November 4 evening batch is identified by ScanTime 17:44:24 ET.
The evaluation CSV currently contains these same-day playable plans; morning
rows are Other/zero-plan diagnostics and are excluded from this denominator.

| Group | Published | Entered | Win | Loss | Open | NoEntry | NoData |
|---|---:|---:|---:|---:|---:|---:|---:|
| BellUp | 8 | 6 | 1 | 0 | 5 | 1 | 1 |
| ReversalHook | 5 | 1 | 0 | 0 | 1 | 4 | 0 |

FSLR (BellUp rank 7) entered at197.63, exited at201.83 at November5 05:55,
yielding2.13% rounded, before commissions/slippage. BellUp rank1 JPM is Open,
with saved forecast98.4%; this is unresolved, not a validated forecast.
Entered BellUp Win share is1/6; resolved Win share is1/1, an insufficient sample
to establish reliability. Reversal rank1 AEVA is NoEntry. SHOP is the only
entered reversal and remains Open. Reversals have no saved Win forecast.

Rows were evaluated around06:24–06:36 ET on November5; observed ranges vary
by row. The report does not contain the following full regular session.
There are399 Other/NoEntry diagnostic rows on November4, not399 failed plans.

## Published reversal plans

All five entries equal their saved ScanPrice; the former deep-discount issue
has not returned. They passed admission and received positive plans.

| Ticker | Entry | Traded M5 range in evaluation | Outcome/reason |
|---|---:|---:|---|
| AEVA | 10.23 | 9.54–9.95 | NoEntry / GappedBelowEntry |
| METC | 6.95 | 6.85–7.05 | NoEntry / NoEntry |
| INTC | 58.47 | 55.91–57.62 | NoEntry / GappedBelowEntry |
| SHOP | 153.46 | 150.73–154.00 | Open; entry November4 17:45 |
| CRMD | 5.66 | 5.74 only | NoEntry / EntryTooDeep |

METC's four positive-volume observations are17:55 at6.90,19:35 at6.94,
November5 04:00 at7.05 and05:00 at6.85. None individually spans6.95.
An aggregate minimum below and maximum above entry do not establish a touch
between sparse trades. Its reference was a live ask at6.95, not a future
execution. CRMD has only two evaluated traded bars, both5.74. Carry-forward
bars at5.66 have zero volume and cannot establish a fill.

Source: Data/logs/log-20261105_0606.log, diagnostics around lines4190,
5049,5898 and6308. Source candidate price times precede publication by
24.78–161.26seconds; freshness alone does not guarantee subsequent execution.

## Evaluation limitations found

CandidateEvaluator excludes any M5 whose start precedes publication. Thus
the17:40–17:45 parent bar is entirely excluded after publication at17:44:24.
Its full OHLC cannot safely establish post-publication participation, but the
current code also does not inspect its remaining suffix with finer data.
This affects both groups. CRMD's cached17:40 bar has positive volume and
range5.66–5.67; TTD's has range13.27–13.29. Neither proves an entry after
17:44:24. TTD's later minimum is13.28 versus entry13.27, explaining its
reported EntryTooDeep without an actual scanner entry discount.

AEVA and INTC cache M5 for November4 ends at17:35, despite the scanner's
positive-volume partial17:40 references. Evaluator traded observations resume
November5 04:00. The historical range loader can infer interior coverage from
a sparse cache; evaluator repair currently targets regular-session gaps only.
Consequently these ranges do not prove that no intervening after-hours entry
occurred. The labels GappedBelowEntry describe available observations, not
a verified continuous overnight path or a confirmed opening gap.

LYFT is NoData: no M5 returned for the evaluation window. Its cache ends at
November4 17:35. Log around6194–6205 records empty historical data, not a
trading failure. Missing data require investigation independently of strategy.

## Recommended follow-up

1. Repair/verify the publication boundary and relevant after-hours history
   with bounded direct requests; distinguish verified sparse trading from
   unverified cache holes. A missing extended-hours slot alone does not prove
   missing data because instruments may have no trades in that slot.
2. Inspect only provably post-publication execution evidence inside the
   publication M5. M1 aggregates straddling publication still cannot establish
   subminute event order; do not count earlier minute extrema as later fills.
3. Specify whether a direct ScanPrice entry models an exact-price touch,
   a buy limit allowing a better price, or immediate executable purchase.
   The current evaluator models an exact future touch. A market-order model
   needs actual subsequent execution pricing, freshness/expiry rules and
   consistent target/stop/profit treatment; it cannot assume a fill at ScanPrice.
4. Reassess entered Win/Loss/Open after adequate coverage. Do not relax
   pattern admission, widen stops or award hypothetical wins from these rows.

Current rules: [evaluation](../../project-skills/ibswingtrader-evaluation/references/EVALUATION_RULES.md),
[direct reversal entry](../../project-skills/ibswingtrader-scanner/references/REVERSAL_SCAN_PRICE_ENTRY.md).
