# TradePlan Scope

Candidate selection, ranking and TradePlan jointly serve the
[current success criteria](../../../guides/success-criteria.md), updated
2026-10-17. Keep their mechanisms distinguishable when locating a defect.

## Minimal near-term goal

A dependable winner at rank 1, with profit of at least 1.5% sufficient.
This supersedes the former minimum capture-more-than-10% goal. It is the
user's requested target, not a demonstrated guarantee. Inspect Win among
entered plans, winning profit percentages and saved confidence calibration.
Do not defer needed execution fixes merely because amplitude is high/low.

## Historical positive example: SECZ (2026-09-08 scan)

The user identified SECZ as the ideal minimum-goal example on 2026-09-09:
regularly put one such playable winner at rank 1; one is enough. Its 10.93%
profit is a historical example, not the current minimum. A long list
or high amplitude alone does not satisfy this goal.

Saved scan: `2026-09-08 07:51:20 America/New_York`. Evaluation on September 9:

- `PatternVerdict=Match`, `DetectedPattern=BellUp`, confirmed on H4.
- Entry `7.41` at `2026-09-08 09:50:00`; exit `8.22` at `14:35:00`.
- `Outcome=Win`, `PlannedProfitPct=10.93`, consistent with the entry/exit
  prices; this is an evaluated plan outcome, not evidence of a broker fill.
- `AmplitudePct=13.52`; `MinFirst`; observed minimum `7.40`, only `0.01`
  below the planned entry.
- `CandidateDisplayRank=4` among admitted Runaway. The original candidate
  CSV has `DisplayRank=11` because it still mixed Other into that pool.
- Saved `DiagnosticsRankingQualityScore=15.08`, estimated hit rate `55%`.

Use this as a positive case of recognition, entry timing and profit capture
under the historical `Runaway` family labels. The scanner now emits the
pattern-first `BellUp` group. Compare its original snapshot with higher-ranked
setups when investigating ranking. Do not hardcode SECZ or promote a shape
from this single outcome without validation across scans. The newer body
timing gate has not been replayed through the full application on this
snapshot. The user's later annotated charts clarify that only the confirmed
H4 pattern should control the body-based timing veto; Daily was only starting
its own pattern that day. Cached H4 small-body ratios still flag a boost,
so preserving this historical winner under the changed code is unresolved.
See the scanner's `SERIES_PLAYBOOK.md` "SECZ chart clarification" before
treating this example as a verified pass under the new gate.

## Diagnose selection, ranking and execution together

Start with the actual rank-1 result and the entered-plan Win/Loss/Open
cohort. Check whether selection missed a useful setup, ranking preferred a
loser, prices were stale, entry was never reached, or the stop/target path
failed after entry. Amplitude and research coverage help explain the path;
neither overrides the actual result or the current minimum-profit objective.

A high-amplitude NoEntry can reveal a deep/mistimed entry, but is not a Win.
A Loss with high amplitude still hurts the primary metric. A modest 1.5% Win
can meet the minimum goal. Profits below 10% do not by themselves establish
an overly conservative exit, especially under final-target reduction.

## Series-driven TradePlan Direction

The trade plan should use available rows/series from both:

- `evaluation-dataset.csv`
- `research_top_gainers.csv`

The goal is to learn/predict:

- whether entry should wait for a pullback or confirmation
- how deep the expected pullback is before the move continues
- whether the setup is min-first, max-first, same-bar, or stale
- where exit should be placed for that setup family

Do not tune entry and exit as one global rule for all strong candidates.
Group high-amplitude rows by setup shape (fast continuation vs. pullback vs.
min-first, etc. — see `SERIES_PLAYBOOK.md` "Main interpretations"), then tune
the entry and exit profile per group. A fast continuation group may need a
shallow entry and earlier exit; a pullback group may need a deeper entry and
different target logic. Do not group by literal series-distance/similarity —
that comparison method was tried for scanner ranking, found not to
discriminate outcomes (see `ibswingtrader-scanner/references/SCANNER_MODEL.md`
"Series-template matching"), and the matching code was removed 2026-09-01.

Useful fields include:

- `RecentDaily*Series`
- `RecentH4*Series`
- `RecentWeekly*Series`
- `ExtremumOrder`
- `EntryDistanceToMinAfterScanPct`
- `MaxPctBeforeEntry`
- `MinPctBeforeEntry`
- `PostMaxDrawdownPct`
- `MinutesFromMinToMax`
- `MinutesFromEntryToMax`
- `MinutesFromEntryToMin`

Do not patch this by only increasing stop size or applying a flat entry discount.
That hides the need for a predictive entry/exit model.

The active direction for entry prediction is four series-driven profiles:

- `FastContinuationShallow`: winner-like rows with no deep-pullback signature should use near-current/shallow entry. If earlier rules made entry too deep, cap the discount.
- `ModeratePullback`: alive but cooling rows should wait for a moderate pullback, not the old deep default.
- `DeepPullback`: deep entry is valid only when Daily/H4 rows show a real below-mean/deep-pullback shape.
- `AvoidLateSpike`: overheated late spikes near highs should require a very deep entry or be skipped by practical non-fill.

Bell pair:

- `BellUp` should behave like the shallow continuation side: the row shape must
  show a prior squeeze / flat phase followed by clear expansion, and the entry
  should stay close to the scan price instead of collapsing into a generic deep
  discount.
- `BellDown` is the mirrored reversal-side shape: use the same
  prior-compression / recent-expansion rows to shape a reversal entry/exit
  profile, but keep it separate from above-mid continuation.
- One clean Bell timeframe is enough. The source timeframe changes the
  expected holding horizon:
  - `H4` => today-style execution
  - `Daily` => tomorrow-style execution
  - `Weekly` => wishlist / next-week execution

Daily below mean is not automatically a deep-pullback entry. If the row shows a
fresh launch from below daily mean with strong H4 acceleration, treat it as
`FastContinuationShallow`; otherwise winners like `AKAN` can become high-amplitude
`NoEntry` rows.

Bollinger squeeze/launch patterns are timeframe-scalable and should influence
the trade profile:

- `Weekly` launch: broad swing background; allow stronger targets only when
  Daily/H4 do not contradict the setup.
- `Daily` launch: stronger swing or next-day continuation profile; TE-style
  moves can justify more ambitious exits.
- `H4` launch without Daily launch: playable early trigger, but usually use a
  faster capture/defensive exit profile rather than assuming full Daily-style
  amplitude.
- `Daily + H4` launch: higher-confidence continuation; entry can be shallower
  and exit can be less conservative than H4-only.

Use the real Bollinger band rows (`*BbUpperBandSeries`, `*BbMidBandSeries`,
`*BbLowerBandSeries`) to identify the pattern. The old distance/width rows are
still useful, but they do not show the same visual curve shape as the chart.

The broader trade-plan direction is to use real chart-like indicator rows for
confidence and ambiguity checks:

- Bollinger real upper/mid/lower lines are the primary pattern shape.
- MACD real line, signal line, and histogram rows are the secondary signal.
- RSI real rows are the last confidence/ambiguity correction.
- MA is not a priority decision signal. Keep existing MA/distance rows as
  legacy/context, but do not add trade-plan complexity around MA unless a
  specific analysis proves it improves decisions beyond Bollinger/MACD/RSI.
- Distance/width rows remain useful for diagnostics and compatibility, but new
  entry/exit decisions should move away from them.

Use these profiles as diagnostic hypotheses, then validate entry/exit changes
against entered-plan wins, profit and rank-1 results. High amplitude alone
does not justify widening stops, raising exits or rejecting modest winners.
Historical positive/negative amplitude template cohorts are research context;
they are not the current target or instructions to restore removed literal
series matching. Confidence calibration must follow the same plan policy.

## Current project principle

Improve the published playable list and its plan against the three ordered
success criteria. Keep BellUp and ReversalHook cohorts identifiable. Changes
must improve actual outcomes rather than merely relabeling a weak rank-1 row,
raising its confidence, or finding a large future maximum.
