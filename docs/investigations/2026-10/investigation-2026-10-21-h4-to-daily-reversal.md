# Do October20 H4 hooks precede Daily recovery?

Recorded 2026-10-21. Offline saved-data audit requested by the user.
No broker calls, application commands, builds or tests were run. The Python
analysis only reads candidate/evaluation/cache data and writes research
artifacts. No scanner rule, forecast, cache or outcome was changed.

## Selection and time boundary

Exact join Ticker + ScanTime + PresetScanCode, scan date October20, saved
H4 Recognized=True and current signed evaluation AmplitudePct>=10. Both
morning/evening scans were considered; all13 qualifying rows were morning
ScanTime04:19:40. All13 were Other/NoEntry with zero published plans.

This uses the report after the user-requested amplitude-reference refresh.
The population therefore differs from the earlier12-row old-range screen:
the current list includes OUST/NESR and excludes BLSH. CRWV's current saved
value is11.62%, versus the previously quoted11.63%; neither difference
changes its inclusion. Future report updates can change this cohort again.

Completed-bar cutoff October21 15:59:59 America/New_York explicitly excludes
the still-forming October21 D1, including any live chart moves. Ten symbols
have the October20 closed D1 in cache; HUT/BLZE/ALM stop at October19.
Thus there is one subsequent closed D1 for10 symbols and none for3, not
several future sessions. Cache availability today does not prove its live
delivery time yesterday; saved candidate reasons remain primary evidence.

## Results

At the morning scan **0/13 had a recognized D1 hook**, while all13 were
below their saved native Daily mid. Scan-price-to-mid room ranged4.62–36.42%.
That establishes a smaller-scale signal under an unfinished Daily picture;
it does not establish that every Daily picture must turn up next.

| Group by subsequent closed-Daily evidence | Tickers | Finding |
|---|---|---|
| Daily recognition subsequently observed | RCUS, CHPT, IMMX, NESR | All four have a new reconstructed October20 D1 mid-bend seed. More strongly, the actual evening17:30:22 snapshots already record D1 Recognized=True. |
| Improving Daily components, no recognized Daily seed | SMCI, CRWV, AXTI, NBIS, OUST | At October20 close, mid decline slows, lower-band decline slows and histogram improves. Lower bands remain descending in these five; this is preparation evidence, not a confirmed upward hook. |
| Mixed/weak Daily development | SPCX | Mid decline slows, but lower-band decline accelerates and histogram worsens; no Daily seed. |
| No subsequent closed D1 in cache | HUT, BLZE, ALM | No transition verdict. ALM's saved scan-time mid, lower band and histogram were all deteriorating. |

For the nine symbols in the first two groups, simultaneous improvement means
the following transparent descriptive conditions, not a new trading gate:

- Mid delta>=0 or latest delta>previous delta (less negative).
- Lower-band delta>=0 or latest delta>previous delta.
- Latest histogram>previous histogram.

All nine still have negative latest mid deltas. The lower band actually rises
only in RCUS/CHPT/IMMX/NESR; it is merely falling less quickly in the other
five. These distinctions matter when interpreting a broad sideways range.
The result supports a staged-recovery hypothesis, not a claim that nine
confirmed Daily hooks or nine winners occurred.

## Actual evening admission is a different question

The four actual evening Daily recognition snapshots remained Other:

- RCUS and CHPT were selected Completed: their local Daily phase was Active,
  but the same-episode H4 completion guard consumed the admission impulse.
- IMMX and NESR were selected Active on D1, then rejected for W1 support:
  IMMX weekly mid slope-3.66%, histogram-0.37; NESR slope-5.50%, histogram-1.65.

The October21 H4-led Daily-support change applies to selected H4 episodes.
It does not relax W1 admission for selected D1 episodes. Do not promise that
all these old Other rows would become playable after that change. Recognition
at a new scale and safe admission are separate checks.

## Reconstruction and data checks

Primary: saved candidate D1/H4 phases, recognition and strict-hook component
flags, saved rounded Daily arrays and scan price. Secondary: native D1 cache
reconstruction under ema20-sma-seed20-popstd20-v1, full available prefix,
SMA20-seeded EMA20,20-close population standard deviation, first-close-seeded
MACD12/26 with EMA9 signal and simple14-close-change RSI. Research code mirrors
the mid-bend and historical strict-hook seed conditions but is not an executed
C# application/publication replay.

Reconstructed pre-scan EMA mids agree with all saved rounded mids within0.005.
Pre-scan reconstructed D1 recognition is False for all13, agreeing with actual
saved recognition. The four reconstructed later seeds agree with actual
evening recognition. Round-to-cents histogram comparisons erase tiny changes
for RCUS/BLZE; the artifact exposes these rounded-snapshot differences instead
of treating the rounded arrays as exact computation inputs.

Python reconstruction does not apply fresh quote admission, trade-plan,
BellUp precedence or cross-frame completion to authorize any trade. It never
uses a final forming October21 Daily candle as a scan feature. Native bars may
contain later corrections; the same reconstruction need not match a Yahoo
chart's SMA center/current partial candle. For example CRWV's cached
October20 EMA mid is76.72; the user chart's current79.26 is a different
formula/observation boundary and is not used to force indicator parity.

Saved amplitude may have incomplete M5 coverage (already documented for
SMCI), and the positive-amplitude selection intentionally excludes weak movers.
There is no matched low-amplitude/control cohort or later multi-session
outcome window. Therefore this audit does not estimate predictive lift,
profitability, an eventual H4->D1 conversion probability, or causation.

## Reproduction and follow-up

Script: tools/analyze_h4_to_daily_reversal.py.
Artifacts: docs/artifacts/analysis-2026-10-21-h4-to-daily-reversal.json and .csv.
JSON retains complete source reasons, metrics, source/cache SHA256 hashes,
raw follow-up candidate evidence and cutoff/selection metadata.

```text
python3 tools/analyze_h4_to_daily_reversal.py --date 2026-10-20 --as-of 2026-10-21T15:59:59
```

For a later follow-up preserve this original cohort and its starting snapshot,
then append newly closed D1 observations for the same13 tickers. Re-selecting
the population by an updated future amplitude would change the question.
Once several subsequent sessions are available, compare with a matched
same-date H4-recognized control cohort before assigning probability weights.
