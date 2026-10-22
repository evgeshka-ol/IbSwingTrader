# October21 evening: eight published ReversalHook plans

Recorded 2026-10-22 from candidates.csv ScanTime2026-10-21 17:23:56,
log-20261021_1615.log and native cached candles. All times America/New_York.
Saved-data inspection only; no application, broker, build or test command
was run and no production rule/outcome was changed by this review.

## Counts and policy evidence

The user initially counted six. The CSV and final log agree on eight
ReversalHook rows,16 BellUp and203 Other; playable total24. The discrepancy
with the user's displayed six has not been attributed to a specific UI/filter.

| Ticker | Selected frame/phase | Saved support | ScanPrice | Entry | Target | Stop | Entry below scan |
|---|---|---|---:|---:|---:|---:|---:|
| GRAL | D1 MidCrossed | W1 BellUp | 132.60 | 130.21 | 140.49 | 122.40 | 1.80% |
| DDOG | D1 MidCrossed | W1 BellUp | 274.00 | 268.17 | 281.58 | 254.77 | 2.13% |
| OKTA | D1 MidCrossed | W1 BellUp | 209.00 | 192.28 | 197.04 | 189.90 | 8.00% |
| CLSK | H4 Active | H4-led recovery | 8.81 | 8.20 | 8.84 | 8.12 | 6.92% |
| IREN | H4 Active | H4-led recovery | 27.86 | 26.19 | 28.26 | 25.92 | 5.99% |
| CIFR | H4 Active | H4-led recovery | 9.15 | 8.58 | 9.25 | 8.32 | 6.23% |
| AESI | H4 Active | H4-led recovery | 9.39 | 8.73 | 9.60 | 8.47 | 7.03% |
| FMC | H4 Active | H4-led recovery | 5.08 | 4.69 | 5.15 | 4.55 | 7.68% |

The five H4 rows explicitly carry Support=H4-led recovery below D1 mid and
fresh provisional momentum provenance. Their saved D1 histograms remain
negative: CLSK-0.31, IREN-1.16, CIFR-0.51, AESI-0.29, FMC-0.32. These are
actual runs of the new branch, not only prepared regression cases. This
establishes changed admission behavior, not profitable executions.

GRAL/DDOG/OKTA have a newly recognized October21 D1 mid-bend seed and an
October21 08:00 H4 seed. Their selected phase already crossed mid. The five
other rows retain D1=None/Recognized=False and H4=Active. CLSK's H4 mid-bend
seed is October21 12:00; IREN/CIFR October20 16:00. AESI/FMC use historical
strict-hook recognition rather than a discrete mid-bend; phase reasons can
carry BarsSinceBend=-1 with recognized historical BarsSinceSignal instead.
FMC's historical seed is five H4 bars old, so it deserves a chart-stage check
rather than being described as a brand-new bend.

Of these eight only AESI has IsFromWishlist=true; the other seven were live
universe discoveries. Admission is not evidence that all are reversal-watch
promotions or all had the same pattern age.

## Remaining checks

The five H4 plans require roughly6–8% pullbacks from their saved scan prices.
Continuation without that pullback can leave NoEntry even after admission is
fixed. OKTA also requires8%, and its target197.04 lies below ScanPrice209;
that is a pullback-then-rebound plan, not an immediately executable growth
target. Do not describe planned profit as available profit from current price.
No entry-profile threshold is changed by this inspection.

FMC's October21 D1 is O5.37/H5.39/L4.82/C5.03 (-6.33% body), while its
last regular H4 recovers4.94->5.04. That is an early local recovery inside
a weak Daily bar, not evidence of completed Daily recovery. The new branch
intentionally permits some such setups; charts and later entered outcomes
must establish usefulness, not the admission count alone.

Previously discussed names in this run:

- AESI: admitted by the new H4 branch.
- CENX: D1 Active, rejected W1 Collapse/Down.
- FTAI: D1 Completed, rejected completed impulse.
- MLI: H4 Active, rejected D1 Collapse/Down; the new branch preserves this veto.
- NFLX and CRWV: Stalled, rejected before higher-frame support.
- SPCX: D1 MidCrossed, rejected W1 support data unavailable.

This review verifies implementation effect only. Subsequent evaluation must
distinguish Win/Loss/Open/NoEntry and preserve quote/publication timing. It is
not a chart-verified quality verdict on the eight candidates or a probability
calibration result.
