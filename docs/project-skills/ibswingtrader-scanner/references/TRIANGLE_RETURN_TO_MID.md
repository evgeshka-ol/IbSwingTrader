# Triangle completion and return to Bollinger mid — pending research

Recorded 2026-10-13 from the user's chart observations. This is a research
direction, not a deployed admission rule or an independently measured hit rate.

## User's observation

The user first recognized Triangle in BTC/USDT charts on Binance in 2023
and reports repeatedly seeing the same behavior across assets and timeframes:
when the figure completes, price is pulled toward the Bollinger middle band.
The user describes this return as practically guaranteed in their experience.
Preserve that attribution; do not reduce the observation to a single ALHC case
or present it as a statistically established guarantee of the implementation.

The discussed falling form consists of a drop followed by consolidation along
the horizontal side of the triangle while the mid band remains above price.
Ordinary consolidation oscillations stay around that level. A late excursion
below the horizontal side can increase the distance to mid and the size of
the subsequent rebound. Such an excursion is an additional feature, not a
required condition for every Triangle.

## ALHC example

User-reported daily chart observations for 2026-10-12:

- Two falling Triangle structures appeared with a short interval between them.
- In the penultimate daily candle before the figure completed, price fell
  strongly below the consolidation's horizontal boundary.
- Premarket reference about 7.40; subsequent regular-session high 8.31;
  Bollinger mid about 8.28. High relative to 7.40 is approximately +12.3%.
- Reaching mid intraday is distinct from closing at/above mid or making that
  return tradable from a scanner-known entry price.

These figures are reported chart values, not a completed cache validation.
Chart references: `Data/Charts/ALHC-D.png` and `Data/Charts/ALHC-W.png`.
The files may later be replaced; the observations here retain the dated context.

Weekly context can remain bearish while the daily figure returns to mid.
The user described a weekly BellDown-like consolidation: the previous pause
lasted six weeks before an impulse in week seven; the current pause was in
its fourth week. A daily rebound does not by itself invalidate that weekly
scenario. Horizons must scale with the timeframe rather than assuming a
next-session outcome for every pattern.

## Checks for later research

- Define horizontal consolidation boundary, onset, and figure completion
  from information available at each decision time. The completion rule
  remains to be specified; do not infer it from a subsequent hit of mid.
- Distinguish this falling/recovery Triangle from the current scanner's
  post-rise `Triangle Growth` heuristic. The shared name does not prove the
  current detector recognizes the user's structure.
- Measure return to the mid known at signal time separately from touching
  the evolving contemporaneous mid; a falling mid can meet price without
  a large rebound. Compare scanner price, consolidation level and excursion
  low as explicitly separate reference prices.
- Compare ordinary completion versus completion after a downside excursion:
  excursion depth, timing, recovery above the horizontal boundary, distance
  to mid, maximum adverse movement before the touch, time to touch and
  realizable entry/target outcomes.
- Include failed recoveries and unfinished windows, with consistent horizons
  in bars and separate results by timeframe. Do not count repeated scans
  of one episode as independent confirmations.
- Replay only bars/quotes available at the signal time; a later completed
  daily/H4 candle cannot justify an earlier scanner decision.

The authorized work here is recording the hypothesis. Production changes,
numeric recognition thresholds and a Triangle trading group are not defined
by this note. Current Triangle remains diagnostic `Other`.
