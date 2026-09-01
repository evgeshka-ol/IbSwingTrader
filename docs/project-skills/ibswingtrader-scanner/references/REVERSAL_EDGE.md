# Reversal: the user's proven manual edge

This file supplements `SKILL.md` and `SCANNER_MODEL.md` with context specific
to the `Reversal` family that does not belong in the general model description:
why `Reversal` gets priority, the precise trigger definition in the user's own
words, and open items that were investigated but deliberately left
unimplemented.

## Why Reversal outranks Runaway in priority

`Reversal` is the user's own trading edge, developed over years of manual
trading *before this app existed* — watching freshly-crashed stocks (top
losers) for days/weeks until one gave the signal, with no scanner or feature
series at all. Personal best trade: **+25.7% in 3 days** (Friday entry,
Monday exit). What historically broke this edge was never the pattern itself
— it was emotion (entering too early/late) and the physical limit of watching
only a few dozen tickers by eye. This app's purpose for `Reversal`
specifically is to remove that emotional dependency and scale past the
broker scan API's ~50-tickers-per-scan-code limit, not to discover a new
pattern.

`Runaway`, by contrast, is newer territory even for the user — noticed only
while building this app, never traded manually on its own, and considered
structurally more complex. A mirror-image "reversed Runaway" would read as a
short signal; the user has decided not to trade shorts (higher risk) and this
is not being pursued.

**Practical consequence: when prioritizing scanner work, default to spending
more attention on `Reversal` detection quality than on `Runaway` tuning.**
The user already knows the underlying pattern works from lived experience —
the open question is only whether the code detects the same bend a
human would see by eye, not whether the pattern itself is real. If
`Reversal` is producing zero or very few admitted candidates across multiple
consecutive real scan days, treat that as a likely recall/detection gap in
the code first, before concluding the market simply isn't offering setups.

## The canonical trigger, precisely

In the user's own words: the mid Bollinger band declines like the
**hypotenuse of a right triangle**, then abruptly bends — transitioning to
horizontal or near-horizontal. **That angle change is the trigger itself**,
not a supporting detail.

- The bend must be a genuine change from a *clearly, steeply declining* prior
  slope. A mid band that reads near-zero both before and after was already
  flat — that is not a bend, it's an absence of trigger. See the `NAT`
  counter-example below.
- "Room to run" (distance from current price to the mid band at the moment of
  the bend) is **not a signal on its own**. If the mid is still declining,
  more room is a warning of a stronger stepped continuation *down*, not an
  opportunity. Room only matters as a **payout multiplier** once the bend has
  already fired — more distance to close after a genuine bend means a bigger
  potential capture (this is exactly how the 25.7%/3-day trade happened). Do
  not use distance-to-mid as an admission filter for `Reversal`.

## Signal hierarchy (Reversal-specific confirmation of the general rule)

This confirms — anchored specifically to how the user judges `Reversal`
entries — the general series-priority order already stated in `SKILL.md`
("Bollinger pattern direction"):

1. **Bollinger** (the mid band's angle change) — primary, necessary. Nothing
   else substitutes for it.
2. **MACD** (line/signal crossover) — strong amplifier. A crossover imminent
   or occurring near the same point as the mid bend meaningfully strengthens
   confidence.
3. **RSI** — third-tier, optional extra confirmation; usually 1+2 already
   suffice on their own.

Distance from price to the mid band is not a signal in either tier — see
above, it only sizes payout after the primary trigger fires.

## Worked example: `NAT` (H4, 2026-08-11)

A candle-by-candle case study, useful as a validation target if a
candle-level Reversal detector is ever built (the current stored-series
fields cannot represent this):

1. **Preparation**: a short red candle, then a short green candle almost
   reclaiming the red candle's open. The *lower* band has already bent
   upward, but the *mid* band is still on its old declining slope — not
   bent yet. MACD is already recovering, approaching a line crossover.
   Reading: bear pressure weakening, a first probing bull attack — not a
   trigger yet, just softening.
2. **First real attempt**: a red candle with huge wicks on both ends. The
   upper wick crosses the mid band (a genuinely playable moment, exit target
   the mid). Bears push back and the lower wick breaks the lower band, but
   it doesn't hold — "bulls have already smelled profit."
3. **Decisive strike**: two long candles in a row. On the *first* of these
   two, the mid band finally bends — that bend fuels the *second* candle's
   strong impulse, which crosses the mid band with real room to spare.
   "Checkmate" — the stock goes sideways afterward.

Why this matters for code: the tradeable structure is *which specific
candle's wick crosses which band*, and *which specific candle is the one
where mid itself bends* (candle 1 of the final two) versus the one that
capitalizes on it (candle 2). None of that survives into a bar-over-bar
band-value delta (`MidPriorSlopePct`/`MidRecentSlopePct`/`MidDeltasTail`) —
those fields can tell you a slope changed, not which candle changed it or
which candle is the payoff. If raw OHLC is ever captured for `Reversal`
detector work, this NAT sequence is a ready-made worked example to validate
a candle-level detector against.

**Counter-example lesson, same day:** `NAT` was initially misread as a
`Reversal` candidate because `MidPriorSlopePct → MidRecentSlopePct` were both
near zero — but the mid band was already flat before *and* after; there was
no angle to change. What actually fired was a MACD crossover at a local low,
a secondary-tier signal on its own, not the primary Bollinger trigger. Do not
read a near-zero `MidPriorSlopePct → MidRecentSlopePct` pair as "just
flattened from a steep decline" — that pattern requires the *prior* value to
be clearly, steeply negative first.

## Open, deliberately-unimplemented items

Same discipline as the rest of this skill's ranking work: validate a signal
empirically (AUC / separation check against `evaluation-dataset.csv`) before
wiring it into admission or ranking, and don't jump straight to retuning
thresholds on a mechanism that hasn't been shown to carry signal at all.
None of the below has cleared that bar yet.

- **Mid-band-bend detector as a discrete curvature check.** If revisiting,
  the existing curvature-detection style in `BellPatternClassifier`
  (`IsBellUpCurveTurn`/`IsBellDownCurveTurn`, which look for a discrete
  point-by-point kink rather than a windowed average slope) is a more
  principled starting point than an arbitrary before/after percentage
  threshold — repeated attempts at fixed numeric thresholds (Weekly, Daily,
  H4) kept producing samples too small or mismatched in scale to draw a
  conclusion. Note: `IsBellDownCurveTurn` itself is not directly reusable
  as-is — it detects the down-*launch* acceleration turn (squeeze-to-expansion,
  mid still steepening), the mirror opposite of the exhaustion/flattening bend
  this section describes.
  - **Tested 2026-09-01** on `RecentDailyBbMidBandSeries` (daily, 30-point
    snapshot) against 260 decided Reversal Win/Loss rows in
    `evaluation-dataset.csv`, target = `AmplitudePct >= 10%`: a windowed
    prior-slope-vs-recent-slope-pct feature scored AUC 0.33-0.48 depending on
    how strict a prior-steep-decline gate was applied (worse than chance as
    the gate tightened); a discrete boolean version (steep decline a few bars
    back, gated at <= -0.3%/bar, then last delta closer to zero) scored
    AUC 0.48 raw / 0.478 as a magnitude. **No exploitable signal at daily-bar
    granularity in either formulation** — consistent with the AUC=0.54
    "band-slope deceleration alone" result already recorded in
    `SCANNER_MODEL.md`, and with the suspicion above that this needs
    candle-level detail. Do not retest yet another windowed-average or
    threshold variant of this same daily-bar-delta idea without raw OHLC —
    the daily/H4 stored series appear to be the wrong granularity for this
    signal, not the specific formula.

- **The admission gate and ranking score themselves, not just the mid-band-bend
  idea, carry no signal (tested 2026-09-01).** `IsReversalHookPattern`
  (`BellPatternClassifier.cs`) — the compound gate actually deciding
  `Reversal` admission today — was tested against 194-260 decided Win/Loss
  `DiagnosticRejected` Reversal rows (available because `EmitAllSeenCandidates`
  logs rejects with real outcomes; see `SETTINGS_MAP.md`). Every one of its 14
  boolean sub-flags (`LowerBrokeDown`, `LowerHooked`, `MidDecelerated`,
  `PriceTurnsTowardMid`, `RsiTurnsUp`, etc.) scored AUC 0.43-0.54 individually
  — noise. The compound gate's pass/fail count (0 of 8 top-level terms failing
  vs. 7 of 8) has no monotonic relationship with outcome either — win rate
  stays flat around 33-42% regardless. `CalculateReversalHookQualityScore`
  (the ranking score used for `Reversal`) scored AUC 0.51 on the same
  population — also no signal. A further sweep of continuous features not
  already covered above (Daily RSI rise-from-trough, MACD line/signal gap and
  its delta, MACD histogram level/delta, mid- and lower-band "bend magnitude"
  as a raw sum-of-deltas rather than a windowed slope-pct, Daily/H4 band-width
  ratio, H4 RSI level) topped out at AUC 0.57 (`H4RsiLast`, the single best),
  still short of this project's own ~0.60 real-signal bar. One concrete
  false-negative from the same data: `INHD` was a real `Win` outcome but was
  rejected by the gate on four of its eight top-level terms
  (`LowerBrokeDown=False, MidContextOk=False, PriceTurnsTowardMid=False,
  RsiTurnsUp=False`) — exactly the kind of case the flat win-rate-by-failure-
  count result predicts should be common, not rare.
  - **Do not retune `IsReversalHookPattern`'s thresholds or
    `CalculateReversalHookQualityScore`'s weights on the strength of this** —
    per the discipline at the top of this section, nothing here has been shown
    to carry signal to retune in the first place; tightening or loosening
    numeric bars on a feature set that scores at chance just moves noise
    around. Per the user's explicit decision (2026-09-01), the next step
    instead is capturing raw candle data (see below) so the actual
    candle-level mechanics this section's `NAT` example describes can be
    tested directly, rather than continuing to iterate on aggregate tail-slope
    features that keep testing at chance.
- **Raw OHLC capture added 2026-09-01.** `RecentDailyOpenSeries/HighSeries/
  LowSeries` (Daily already had `RecentDailyCloseSeries`) and
  `RecentH4OpenSeries/HighSeries/LowSeries/CloseSeries` (H4 previously had no
  raw price series at all, only Bollinger/MACD/RSI aggregates) are now written
  to both `candidates.csv` and `evaluation-dataset.csv`, index-aligned with
  each timeframe's existing Bollinger series. This is purely additive — no
  admission/ranking logic changed. Purpose: directly enables testing the `NAT`
  worked example above (which specific candle's wick crosses which band) once
  enough scans accumulate with this data present; every scan going forward
  captures it, but historical rows before this date do not have it
  backfilled.

See `SCANNER_MODEL.md` for a related open item on the `Runaway` side
(intraday reversion while a candidate is still top-ranked live).
