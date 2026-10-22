# SPCX follow-up: later H4 continuation and subsequent Daily recognition

Recorded 2026-10-22 after the user's chart chronology clarification. Sources:
current Data/cache/SPCX.json and saved candidates.csv. No application, broker,
build or test run. This adds later observations to the fixed October20 cohort;
it does not rewrite the October21 audit's earlier data cutoff or result.

## Native H4 chronology

| Bar start, America/New_York | Open | High | Low | Close |
|---|---:|---:|---:|---:|
| October20 12:00 | 147.16 | 148.26 | 146.25 | 147.95 |
| October20 16:00 | 147.95 | 160.49 | 145.19 | 159.70 |
| October21 04:00 | 161.00 | 163.96 | 157.78 | 159.46 |
| October21 08:00 | 159.35 | 166.18 | 158.20 | 163.13 |
| October21 12:00 | 163.13 | 163.48 | 156.93 | 159.27 |

The chart observation is consistent with the stored OHLC: modest movement in
the last regular-session H4, major expansion after the regular close, then a
maximum166.18 in the October21 08:00–12:00 bar. A native bar only locates the
peak inside that interval, not at exactly08:00. Cached M5 is incomplete and
its observed peak163.64 is lower, so it cannot refine the166.18 timestamp.
This is price-path evidence, not a replayed entry/stop/target or traded Win.

## Saved decisions

- October20 04:19:40: H4 recognized/Active, ScanPrice147.06, Other;
  rejected D1 support, mid153.89, histogram-2.14.
- October20 17:30:22: H4 recognized/MidCrossed, ScanPrice154.76, Other;
  rejected D1 support, mid153.31, room-0.94%, slope-0.78%, histogram-2.17.
  Price had already reclaimed Daily mid despite weak closed-Daily indicators.
- October21 04:24:09: H4 recognized/Completed, ScanPrice161.68, Other;
  rejected EpisodeRecognized=True, Phase=Completed. This does not claim that
  later price cannot rise, only that the current upper-band impulse was spent
  under the scanner's admission rule.
- October21 17:23:56: **D1 recognized/MidCrossed**, ScanPrice158.83, Other;
  rejected W1 support data unavailable.

## What this changes in the research interpretation

The original October21 audit only had October20 as the next closed Daily bar.
SPCX's closed October20 D1 then had weakening lower band/histogram, so the
mixed/weak label was accurate for that boundary. It did not prove that a
developing H4 recovery could not precede a later Daily turn.

Native October21 D1 is now O160.56/H166.18/L156.93/C159.31, and actual saved
evening classification recognizes D1. SPCX therefore becomes the **fifth**
member of the original13-symbol cohort with a later saved D1 recognition,
in addition to RCUS/CHPT/IMMX/NESR. This updates SPCX only; the other12 members
have not been uniformly re-audited at the new boundary. It is not a final
cohort conversion rate or proof of win probability.

The October21 H4-led Daily-support branch applies below Daily mid with>=2%
remaining room. It does not itself cover SPCX's October20 evening above-mid
reclaim (negative room), nor selected D1 hooks rejected for missing Weekly
data. Those are remaining admission scopes, not claimed fixed by that branch.
No new relaxation is implemented by this follow-up.
