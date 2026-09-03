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

**Follow-up (2026-09-03) — why the daily-mid gate structurally cannot see
this trade even in principle.** Reconstructed this exact `NAT` H4 window
straight from `Data/cache/NAT.json` (dates line up exactly with a chart the
user drew this box on: `08-05 12:00 C=6.12` low, `08-06 08:00 C=6.42` the
bounce candle). The system's own `evaluation-dataset.csv` has **no candidate
row for `NAT` anywhere near 08-05/08-06** — that bounce never got logged, the
ticker simply wasn't surfaced that day. The only `Reversal` row for `NAT` is
five days later, `ScanTime=2026-08-11 11:34:32`, triggered by a *new, deeper*
intraday low (`08-11 08:00 L=6.03`, below the 08-05 low of 6.12) — and even
that admitted row came back `PatternVerdictReason=not-below-daily-mid`
(Mismatch). Why: the gate checks the close of the *previous completed* daily
bar against the daily mid — but the actual capitulation to 6.03 happened
*intraday, same-day* (08-11), so no completed daily close was ever below mid
by the time the gate ran (the prior day, 08-10, closed at 6.43, still above
a 6.36 mid). **This is a structural blind spot, not a threshold-tuning
problem**: a same-day V-shaped intraday drop-and-bounce can never satisfy a
"previous day's close below mid" rule, no matter how the threshold is tuned,
because the rule can only ever look at bars that have already fully closed.
This is the concrete case motivating the 2026-09-03 architecture decision
below (layered W→D→H4→M15 funnel + graded exemplar similarity) — a Daily-
scale gate checking only completed-bar closes will keep missing exactly this
shape of trade.

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
- **Raw OHLC capture added 2026-09-01, but not actually landing in
  `evaluation-dataset.csv` (checked 2026-09-03).** `RecentDailyOpenSeries/
  HighSeries/LowSeries` and `RecentH4OpenSeries/HighSeries/LowSeries/
  CloseSeries` were added as fields and the code path exists
  (`CandidateFinder.BuildRecentFeatureSeries`, ~line 4224), but across all
  2925 rows of `evaluation-dataset.csv` these fields are non-empty in only
  ~4.8% (140 rows) — all sharing one single `ScanTime` batch
  (`2026-09-01 08:53:57`), not the normal per-scan write path. Root cause:
  the `Reversal`-specific series builder, `BuildReversalPatternSeries`
  (~line 4262), only ever sets `DailyCloseSeries` + Daily BB/RSI/MACD — it
  never sets Daily Open/High/Low and never touches H4 raw candles at all
  (H4 indicators for `Reversal` come from a separate remap,
  `BuildH4ReversalPatternSeries`, that copies H4 BB/RSI/MACD into the same
  object's "Daily-named" fields but still no H4 OHLC). `Runaway` rows show
  the same near-total gap too, so this isn't `Reversal`-only. **Until this
  write-path gap is fixed, do not rely on the CSV for raw candles** —
  reconstruct them directly from `Data/cache/<TICKER>.json` filtered to
  `<= ScanTime` instead, the way `tools/Backfill-ReversalCandles.ps1` and the
  `NAT` reconstruction above do. The Bollinger/MACD/RSI derived series, by
  contrast, are reliably present in every row.

## Architecture decision (2026-09-03): layered funnel + graded exemplar similarity

Agreed direction to replace `IsReversalHookPattern`'s binary gate (which the
`NAT` case above proves is structurally blind to same-day intraday
capitulation drops, not just under-tuned). Two pieces, both still
unimplemented and unvalidated — neither gets wired into admission/ranking
before clearing the same AUC/holdout/asymmetry bar as every other feature in
this project:

1. **Layered timeframe funnel**, in this exact role order (the user's own
   years-long manual method, pre-dating the app):
   - **Weekly** — macro risk gate only, not a pattern check. Down trend =>
     don't enter regardless of pattern (dangerous to hold overnight). Up
     trend => safe to enter and wait if today's target isn't hit.
   - **Daily** — confirms or defers the setup ("play this" vs "not today").
     This is where a real multi-day shape search belongs, not a single
     completed-bar threshold like the current `not-below-daily-mid` check.
   - **H4** — same-day timing decision: can this finish today, or is it
     today-into-tomorrow.
   - **M15** — pure execution: exact entry/exit/stop numbers. Never a
     go/no-go decision layer.
2. **Graded shape-similarity to a small, hand-curated exemplar chart
   library** (currently DKNG, IBM, TSN, EPAM, PYPL, PSQL — expected to grow
   over time) instead of a binary threshold/gate. Must be scored as a
   continuous similarity measure and validated via AUC against real
   outcomes — never as pass/fail template matching.

**Why this differs from July's already-disproven series-similarity
approach** (`CalculateNormalizedPointDistance`/`CalculateGroupDistance`,
removed 2026-09-01, see `SKILL.md` "Series-template direction" — 0/355
templates ever matched across 7 tests): that approach already normalized for
price/scale, so lack of normalization isn't why it failed. The difference
this time is (a) a small human-verified exemplar set instead of 355
auto-generated, uncurated historical windows, and (b) treating similarity as
a graded score to validate via AUC rather than a binary matched/didn't-match
gate. If this new attempt degrades back into a strict template-match
boolean, expect the same failure mode as July.

See `SCANNER_MODEL.md` for a related open item on the `Runaway` side
(intraday reversion while a candidate is still top-ranked live).
