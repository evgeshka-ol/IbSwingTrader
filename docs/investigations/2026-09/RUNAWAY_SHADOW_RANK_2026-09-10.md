# Runaway shadow penalty experiment

Implemented and executed `tools/analyze_runaway_shadow_rank.py`. This is
an offline alternative ranking, not a new application feature. It only
reads CSV files and prints JSON. No candidate data, live ranking, settings,
trade plans or application tests were changed/run.

## Fixed experiment

Use admitted Runaway only, preserving each scan's complete candidate pool.
Require aligned saved H4 close/middle/upper/lower arrays, saved score and
an evaluation with amplitude for every candidate. Exclude incomplete scans
instead of silently dropping contenders. Use the saved ScoreNextDayRank,
which already contains the quality adjustment; do not add quality twice.
Check that descending saved scores agree with DisplayRank before proceeding.

With C=close, M=middle band, W=preceding upper-minus-lower width:

    shrinkage = (M[t]-M[t-1]) - (C[t]-C[t-1])
    severity = clip(shrinkage / (0.10 * W), 0, 1)
    shadow_score = saved_score - strength * severity

Severity is zero unless the middle band rises and price remains above it.
Full penalty corresponds to a one-bar gap reduction of 10% of prior width.
Test fixed strengths 25, 50 and 100 adjusted-rank points, equivalent to
0.5, 1 and 2 quality points under the current multiplier of 50. These are
exploratory scales, not fitted optima. Ties retain original display order.
No current partial-H4 candle is reconstructed or substituted.

## Coverage and results

11 complete scans on six dates (September 1, 2, 3, 4, 8 and 9). Another
130 scans were excluded as incomplete or having fewer than two contenders.
Historical builds and confirmed timeframes differ. Primary day-level
summary uses the latest eligible scan on each date, not multiple rescans
as independent evidence. All-scans results are a sensitivity view.

For all three strengths:

- Top-1 never changes in any of the 11 scans.
- Consequently top-1 outcomes and amplitude do not improve or deteriorate.
- No >10%-amplitude candidate or evaluated Win is moved down in this sample.
  This is not proof of safety on other dates.
- Across all 11 scans, top-1 mean amplitude stays 7.188%, with three >10%
  and one Win. Across the six latest-per-day scans it stays 7.843%, with two
  >10% and no Wins. These are different denominators, not contradictory.
- On the later September 8-9 daily slice, mean top-1 amplitude stays 4.745%,
  with no >10% cases and two Open outcomes. Open is not a final loss.

Some lower places change. At strength 50:

- Winning SECZ September 8 07:51:20 rises from fourth to third, replacing
  HOOD there. SECZ amplitude is 14.18% in the current updated evaluation.
- SECZ September 9 06:21:46 also rises from third to second; it remains
  Open in evaluation. The penalty is not a general SECZ exhaustion detector.
- At September 9 09:49:58, the entire order remains unchanged. SECZ takes
  a 45.28-point penalty, ALM 50, SMR 47.43 and SMTC 28.73; their similar
  warnings and existing score gaps preserve SECZ's fourth place.
- Other rearrangements are mixed: for example MT (0.86% amplitude) replaces
  CRCL (2.03%) in second place in the September 4 10:16 scan.

At strength 100, SMTC and SMR swap second/third in the final September 9
scan, but ALM remains first and SECZ remains fourth. Do not increase the
penalty until a desired example reaches the desired position: that would
be outcome-guided tuning rather than validation.

## Decision

No evidence of top-1 improvement at these fixed modest penalty scales.
There is one favorable SECZ rearrangement but no demonstrated general
ranking benefit. The September 8-9 slice was already examined in prior
work, so it is a later-date check, not a pristine holdout.

Keep this as a reproducible offline shadow calculation. Do not apply the
penalty to production ranking from this result. More timestamped snapshots
and new dates are needed before a fixed rule can be tested prospectively.
