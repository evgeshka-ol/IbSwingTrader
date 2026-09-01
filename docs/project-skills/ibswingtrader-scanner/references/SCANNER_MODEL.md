# Scanner Model

## Main purpose and current priority

See `SKILL.md` "Core intent" for the canonical, current statement of scanner
purpose and the top-1-quality priority (`Runaway` #1 row should consistently
capture >10%, `AmplitudePct` is the scanner-quality oracle, next-day
`research_top_gainers.csv` coverage is the main feedback loop). Not restated
here to avoid drift between two copies — this file covers model detail,
failure modes, and implementation/validation history instead.

## Two candidate families

### Reversal

Use reversal logic only when the ticker is below the daily Bollinger mid.

Hard rule:

- Use the last closed daily bar for this check, not the current intraday
  partial bar.
- Resolve that closed-bar date from the dominant latest D1 date across the
  current scan universe. A market holiday must not be treated as a missing bar.
- If the ticker was below the daily Bollinger mid on the last closed daily bar,
  it is a `Reversal` candidate.
- If the ticker was at or above the daily Bollinger mid on the last closed
  daily bar, it is not a
  `Reversal` candidate.
- Weekly and H4 context may refine the reversal subtype or trade plan, but they
  do not change the family assignment.

Typical traits:

- below-mid or recent return toward mean
- pullback / collapse / early turn
- deeper or delayed entry may be acceptable

Expected quality:

- should still produce `AmplitudePct > 10%` often enough to matter
- lower amplitude is scanner failure, even if the trade plan avoided entry

Final `Reversal` promotion currently requires `ReversalHook` after the hard
below-mid split. The pattern is daily-row based:

- lower Bollinger band breaks down, then hooks upward
- daily mid weakens but decelerates or starts turning
- the channel compresses after the breakdown
- MACD histogram turns upward toward zero
- MACD line and signal converge
- RSI recovers from the recent low

After the hook passes, literal series similarity to a high-amplitude reversal
template on real closed D1 or saved H4 rows is supporting evidence, not a hard
admission requirement. Weekly rows remain context only.


Use POET, ASM, SSRM, CDE, and SVM from the 2026-06-15 evaluation set as the
initial working examples. The cleanest entry is normally one to two daily bars
after the lower-band hook.

### Runaway

Use continuation logic when the ticker is at or above the daily Bollinger mid
and acting like a live winner.

Typical traits:

- above-mid on `Weekly/Daily/H4`
- positive or recovering `MACD`
- supportive Bollinger width / distance structure
- strong live preset such as:
  - `HOT_BY_VOLUME`
  - `MOST_ACTIVE`
  - `TOP_PERC_GAIN`
  - `TOP_OPEN_PERC_GAIN`

Internal subtypes inside this family:

- `BellUp`
- `Runaway`
- `LaunchContinuation`
- `PullbackContinuation`

Expected quality:

- should be the closest proxy for next-day `research_top_gainers.csv`
- misses here are the first thing to fix

Important:

- Treat the family boundary as fixed once the daily mid split is correct.
- Further scanner work should change only whether a candidate reaches the final
  list, how it is ranked, and how the trade plan is shaped.
- Do not reopen the family split when the problem is really profit capture or
  top-of-list quality.

## Evaluation principle

For scanner quality:

- `AmplitudePct >= 10%` is strong
- low amplitude is bad scanner quality
- `NoEntry` alone is not enough to blame the scanner
- if roughly two thirds of candidates clear `AmplitudePct >= 10%`, preserve
  that high-amplitude pool and focus scanner work on rejecting the remaining
  low-amplitude third by row similarity
- if live scanning still needs a late guard, use a row-based envelope-expansion
  proxy as the last gate before the final list; do not use the realized
  `AmplitudePct` itself because it is unknown at scan time

For trade-plan quality:

- count of `Win` rows shows how many scanner opportunities the plan converted
- many `Loss`, `NoEntry`, or still-`Open` rows with strong amplitude indicate trade-plan failure
- do not use the number of wins alone to judge scanner quality

## Common failure modes

### Fully missed

The ticker is absent from logs and `WishList`.

This is recall/universe/filter failure.

### WishList only

The ticker is seen but does not get promoted.

This is promotion or gating failure.

### In candidates but not summary

The ticker exists in `candidates.csv` but not in current top-ranked rows.

This is ranking failure.

### In top but weak amplitude

This is stale/weak ranking and should be penalized.

## Current project direction

The current direction is:

1. use series as the primary signal
2. make `Runaway` predict tomorrow's research dataset
3. keep `Reversal` separate
4. require `Reversal` to produce meaningful amplitude too
5. only then tune `TradePlan`

## Series-template matching

The scanner should move toward literal row-shape matching.

For `Runaway`:

- use evaluation rows only to identify historical scan keys that produced
  confirmed `BellUp` and high amplitude
- load the actual positive template series from the matching historical
  `candidates.csv` scan snapshot
- compare against scan snapshots whose later evaluation had low amplitude as
  negative templates;
  candidates whose rows look like the low-amplitude third should be demoted or
  filtered before they occupy the top of the list
- treat the TE-style fresh expansion as a high-priority winner pattern:
  - weekly MA row recovers from negative/below-mean history into positive territory
  - daily MA, daily Bollinger width, and daily RSI expand together
  - H4 MA, H4 Bollinger width, and H4 RSI also expand and hold
  - weekly/daily/H4 MACD are recovering or positive
  - this pattern can outrank a conflicting low-amplitude template match because
    the row structure matches a practical next-day winner
- treat real Bollinger band curve launches as scalable patterns:
  - use `*BbUpperBandSeries`, `*BbMidBandSeries`, and `*BbLowerBandSeries`
    alongside width and upper-distance confirmation
  - the same squeeze/launch shape can matter on Weekly, Daily, or H4
  - Daily launch is stronger next-day research evidence; H4 launch is often an
    earlier intraday/next-day trigger; Weekly launch is broad swing background
  - do not discard a candidate only because the pattern appears on H4 rather
    than Daily; adjust ranking/trade profile by timeframe instead

For `Reversal`:

- use confirmed high-amplitude `ReversalHook` evaluation labels, then load the
  feature rows from the matching historical scanner snapshots
- do not let above-mean continuation templates promote reversal candidates

Comparison principle:

- evaluation provides labels, never template feature values
- compare each series point-by-point with modest tolerance
- normalize each compared series from its first point
- calculate a separate distance for Daily and H4; matching either timeframe is
  sufficient, and the weighted average must not decide admission
- keep Weekly rows as context only; a Weekly-only match must not promote a
  candidate into today's final list
- avoid replacing this with only slope/aggregate statistics
- split templates by outcome role: high-amplitude rows are positive scanner
  templates; low-amplitude rows are rejection templates

Bell pattern pair (`BellUp`/`BellDown`, matched on H4/Daily only): full
geometry, counter-examples, and the `ReadyNow`/`NotReady`/`Neutral` phase
split are canonical in `SERIES_PLAYBOOK.md` — not restated here.

For the current strict `Runaway` pipeline, final promotion requires `BellUp` on
real Bollinger rows in `H4` or `Daily`. Literal Daily/H4 winner similarity
supports ranking, while a closer low-amplitude BellUp match vetoes promotion.
Weekly-only Bell and other continuation subtypes remain context.

If strict promotion leaves both final families empty, keep the result empty.
The low-amplitude template veto represents observed evaluation feedback and
must not be disabled merely to populate the output.

## Shared classifier implementation status (2026-08-10)

`BellPatternClassifier` (`Application/Candidates/BellPatternClassifier.cs`) is
now the single implementation of Bell/ReversalHook classification, the real
Bollinger envelope math behind it, and the generic slope/delta helpers it
depends on. `CandidateFinder.cs` (live scan) and
`CandidatePatternVerdictService.cs` (offline evaluation) both call into it
instead of keeping parallel copies. This closes a real drift that had already
happened between the two paths (the live `IsReversalHookPattern` required
`lowerHookFresh` and `priceTurnsTowardMid`; the offline copy silently skipped
both) — the shared class kept the stricter live behavior as canonical. Any
future change to Bell/ReversalHook rules belongs in this one file.

## Ranking implementation status — superseded (2026-07-11), then replaced (2026-07-13)

The comparison principle above was briefly implemented as a strict tiered
sort: `Confirmed` template match > `Weak` template match > `None`, with the
matched template's `AmplitudePct` as the primary sort key inside a tier.

**This tiered sort was empirically disproven on 2026-07-13**: on a real scan
every candidate came back `TemplateRankTier=None` even though hundreds of
templates loaded correctly. Root-caused across seven independent comparison
variants (raw distance, %-normalized distance, RSI-only, level-correlation,
first-diff-correlation, worst-of vs avg-of-lines, mild vs extreme amplitude
contrast) — literal point/curve comparison of historical Bollinger/MACD/RSI
series does not discriminate future high-amplitude winners from losers in
this dataset. The low-amplitude veto logic described above is still real
*mechanically*, but tuning `FullMatchDistance`/`WeakMatchDistance` will not
fix a ranking problem, because there was no signal there to threshold on.

Current ranking (since 2026-07-13) is a validated, template-free quality
score instead: `CalculateRunawayLaunchQualityScore` (Daily mid/upper-band tail
slope + H4 mid-band tail slope) for `Runaway`, `CalculateReversalHookQualityScore`
(Daily band-width compression + lower-band hook tail slope) for `Reversal`.
Both were individually validated by AUC against `evaluation-dataset.csv`
before being wired in — Daily/H4 Bollinger band slope was the strongest
finding of the investigation (AUC 0.64 mild → 0.84 extreme contrast).
`ReRankCandidates` sorts by `qualityScore * 50 + legacyNextDayRank`; the
template tier/distance machinery (`ResolveTemplateRankTier`) still runs and
still writes `Diagnostics.TemplateRankTier` etc. for audit, but no longer
affects sort order. Both `Runaway` and `Reversal` still rerank across their
full candidate pool per scan (no top-window cap). See
`ReRankCandidates`/`CalculateRunawayLaunchQualityScore`/`CalculateReversalHookQualityScore`
in `CandidateFinder.cs`.

If extending ranking further, validate a candidate feature's AUC against
realized `AmplitudePct` before wiring it into a quality-score function —
that discipline is what caught the 2026-07-11 approach not working.

## Runaway quality-score feature sweep (2026-09-01)

With Runaway top-1 already hitting `AmplitudePct >= 10%` in 74% of scans
(vs Reversal's 52%), ran a feature sweep to push it further before touching
`Reversal` — AUC of each candidate feature independently against 422 decided
Runaway Win/Loss rows in `evaluation-dataset.csv`, target
`AmplitudePct >= 10%` (replicating `CalculateTailRelativeSlopePct` exactly):

| feature | AUC alone |
|---|---|
| current formula (baseline) | 0.620 |
| **H4 upper-band tail slope (lookback 4)** | **0.673** |
| H4 mid-band tail slope (lookback 6, vs current lookback 4) | 0.661 |
| **H4 band-width expansion (lookback 4)** | **0.639** |
| H4 RSI level (last point) | 0.606 |
| H4 MACD histogram (last point) | 0.608 |
| Daily band-width expansion (lookback 4) | 0.615 |
| Daily upper-band tail slope (lookback 6) | 0.590 |
| Weekly mid/upper-band tail slope | 0.574 / 0.570 |
| Daily/H4 RSI slope, Daily MACD histogram slope | 0.556 - 0.607 |
| Daily RSI level, Weekly MACD histogram (last point) | 0.538 - 0.540 |

H4 upper-band slope was not previously used at all (only H4 mid was). Added
both H4 upper-band slope (weight `0.75`) and H4 band-width expansion (weight
`0.25`) to `CalculateRunawayLaunchQualityScore` — combined AUC 0.620 → 0.692
on the full 422-row sample, and confirmed on a chronological split (first
half by `ScanTime`: 0.667 → 0.727; second half: 0.565 → 0.654) so the gain
is not concentrated in one period. Weekly-timeframe features and RSI/MACD
(second/third priority per the Bollinger>MACD>RSI signal hierarchy) all
tested weaker than the Bollinger-band features and were not added — matches
the existing signal-priority rule rather than contradicting it.

`EstimateHitRatePct` Runaway buckets were recalibrated to the new score
scale the same day: `>=25 -> 85%` (empirical 88.9%, n=45), `>=10 -> 55%`
(empirical 53.8%, n=93), else `30%` (empirical ~31%, n=284). The old buckets
(`>=20 -> 70%`, `>=10 -> 65%`, else `10%`) were calibrated to the pre-change
score scale and would have under/over-stated confidence against the new
formula's generally larger scores. Reversal's buckets are untouched and still
reflect the original 2026-07-14 check (n=19) — recheck those the same way
before trusting them.

## Band-kink / RSI-rollover investigation (2026-08-11)

Origin: the user's working heuristic from live chart reading on 2026-08-10 —
a band's slope decelerating/kinking is an early reversal warning, symmetric on
the upper and lower bands, with a kink/break of the **mid** band treated as
the more decisive of the two. Two grounding examples: `HELP` (Daily upper-band
delta decelerated +0.58→+0.66→+0.71→+0.55 alongside Daily RSI rolling from a
peak of 86.26 to 83.52, a full day before price cracked) and `DKNG` (H4, two
successive lower-band deceleration kinks preceding a mid-band break — working
name `ChannelReclaim`, never formalized past a draft).

Validated against `evaluation-dataset.csv` on 2026-08-11 (285 decided Runaway
Win/Loss rows) by decomposing the heuristic into independent pieces:

- **Daily RSI rolled over from its own recent peak: AUC=0.61** — real,
  comparable in strength to the Daily/H4 slope features already driving
  `RankingQualityScore`. **Implemented**: `IsLateBellUpPhase` now checks this
  directly (`recentDailyRsiPeak >= 70m && dailyRsi[^1] < dailyRsi[^2]`),
  independent of the H4-based branches and of whether H4 bands are still
  expanding — this is exactly the HELP gap, closed.
- Band-slope deceleration alone (the "kink" itself, independent of RSI):
  AUC=0.54 — essentially no signal on its own. Not implemented.
- The `DKNG`/`ChannelReclaim` side (H4, on the Runaway family — DKNG was
  Runaway, not Reversal, despite reading like a reversal pattern):
  lower-band "un-kink" alone AUC=0.48 (no signal), H4 RSI turning up alone
  AUC=0.575 (weak). The full multi-candle criterion (double kink + mid-band
  break) could not be tested — `evaluation-dataset.csv` stores only Bollinger/
  MACD/RSI series, no raw H4 OHLC, and the pattern needs candle-level detail.
  Not implemented; would need raw candle history to properly test.

**How to apply:** the RSI-rollover piece is real and now live in
`IsLateBellUpPhase`. Do not also add a standalone "any band slope kink" filter
— that piece tested at chance level on its own. If `ChannelReclaim` comes up
again, it still needs raw H4 candle data before it can be tested, not just
more evaluation-dataset rows.

A related but distinct daily-bar mid-band feature — the `REVERSAL_EDGE.md`
"mid-band bend" trigger for `Reversal` — was tested the same way on
2026-09-01 (see that file's "Open items") and also came back at chance
(AUC 0.33-0.53 across several formulations). Same conclusion as above: no
signal at daily-bar granularity for a windowed/discrete slope-delta version
of this idea; candle-level data would be needed to test it properly.

## Known unvalidated gap: intraday reversion while still top-ranked (2026-08-10)

`RankingQualityScore` is computed from completed Daily bars plus the current
H4 bar's tail slope only, so it cannot see a same-day intraday reversal
happening *within* the still-forming H4 bar. `AXTI` was the #1 admitted
`Runaway` in two same-day scans with an *identical* quality score even
though live price had already fallen 8.6% between the scans (and kept
falling, -13.6% from the day's open by the time it was checked); `HELP` and
`CRSR` showed the same mid-reversion pattern at smaller magnitude the same
day. The existing safety net, `IsLiveRunawayStructureInvalidated`
(`CandidateFinder.cs` — M15 price crossing below the H4 mid), did not fire in
this case because the H4 mid was itself still rising fast and hadn't been
dragged down enough yet to cross.

Tried to validate "does post-scan price weakness predict `Loss`" against
`evaluation-dataset.csv` before touching any code, and got genuinely
inconclusive results both ways: bucketing by the `MinPct` field looked like a
dramatic gradient, but `MinPct` is computed from candles *after* entry, so a
deep `MinPct` is close to a restatement of "the stop got hit" — circular.
Re-bucketing by the non-circular `MinPctBeforeEntry` (drawdown between scan
and actual entry) instead gave a noisier signal on too few rows (n=7 in the
deepest bucket) to trust. Neither field actually measures the AXTI phenomenon
precisely anyway (reversion toward the mean *while still top-ranked live*,
before any entry/exit window exists) — that would need a purpose-built
feature such as price drift between two same-day scans of the same admitted
ticker.

**Do not add an intraday-drawdown invalidation check on the strength of this
alone** — it is genuinely unvalidated, not "probably fine." Re-run the
`MinPctBeforeEntry` bucket check with a larger sample once more multi-scan
same-day evaluation history accumulates, and specifically re-check what
happened to AXTI/HELP/CRSR.
