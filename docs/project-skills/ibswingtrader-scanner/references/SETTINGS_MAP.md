# Settings Map

Main settings file:

- `IbSwingTrader/agentsettings.json`

## High-impact scanner settings

### `GetCandidates.PreFilter`

Use for universe hygiene and garbage filtering.

Important examples:

- recent daily price floor
- low-price / penny rejection

Be careful: these filters can improve safety but also reduce recall.

### `GetCandidates.WishListFilter`

Controls who can enter `WishList`.

Use this to manage early scan breadth.

### `GetCandidates.CandidateFilter`

Controls conversion from `WishList` to candidate.

Useful when too many names are seen but not promoted.

### `GetCandidates.Finder`

- **Current output categories (since 2026-09-23):** `BellUp`, `ReversalHook`,
  and `Other`. The migration is recorded in commit `d4a4f1d`.
  - BellUp and ReversalHook are the playable pattern groups. `Other` retains
    unconfirmed or confirmed-but-not-ready diagnostics, with a rejection
    reason and no active trade plan. `CandidateGroups.IsOther` also recognizes
    legacy `DiagnosticRejected` sources.
  - Pattern classification is independent of the former Daily-mid
    `Runaway`/`Reversal` split. BellUp is checked first; a BellUp that fails
    readiness remains `Other` and is not relabeled ReversalHook.
  - Current console and CSV output use the new group names. Historical CSV
    rows may still contain `Runaway`/`Reversal`; join candidate and evaluation
    records by ticker and scan timestamp, and prefer their saved
    `CandidateGroup`, `CandidateSource`, and `PatternVerdictReason` over
    inferring the pattern from legacy family fields.
  - Playable rows use `CandidateSource=BellUp` or `ReversalHook`; rejected
    rows use `Other` (older rows can retain `DiagnosticRejected`). The writer
    places the categories in separate console/CSV sections.
  - Rejected setups are retained regardless of `EmitAllSeenCandidates`. The
    setting still affects scan-context breadth and deduplication.

- `EmitAllSeenCandidates`: **deliberately `true`, not a bug.** The current
  behavior retains rejected candidates under `Other`, separately from the
  playable `BellUp` and `ReversalHook` sections. The setting still affects
  scan-context breadth and deduplication; it is not a switch for hiding all
  rejected diagnostics.
  - Origin: earlier rounds of tightening the admission filter kept collapsing
    the visible list to empty or 1-2 tickers, while the broker and outside
    sites (e.g. Yahoo Finance) kept showing real high-amplitude movers on the
    same days — a sign the filter was cutting recall, not just noise. The user
    made this deliberate: return every ticker, ranked best-to-worst by the
    scanner's own quality opinion, rather than let a filter silently hide
    winners.
  - **Do not treat "rank #1 is sometimes a rejected candidate" as something to
    fix by adding a filter here.** The condition for turning this back to
    `false` (i.e. letting the admission gate decide what's shown, not just
    ranking) is empirical and set by the user: only once the top of the list
    is *consistently* landing on `AmplitudePct >= 10%` winners. Until then,
    ranking quality (`RankingQualityScore` / `AdjustedRank`) is the lever to
    improve, not admission strictness.
  - Historical 2026-09-01 rank-1 analysis used `Runaway`/`Reversal`; its
    percentages describe the old family model and must not be read as current
    BellUp/ReversalHook group performance. Recompute on current group labels
    before using those figures for a present-day decision.

### `GetCandidates.NextDayRanking`

Controls final ordering.

Use this when the right names are present but top summary is wrong.

Since 2026-07-13, final order is driven by a template-free quality score
(`CalculateRunawayLaunchQualityScore` / `CalculateReversalHookQualityScore` in
`CandidateFinder.cs`, weighted by the hardcoded `QualityScoreRankWeight = 50`
constant, not a settings knob). See `references/SCANNER_MODEL.md` "Shared
classifier implementation status" / `SKILL.md` "Ranking (current state)" for
what actually decides order today.

### `GetCandidates.NextDayRanking.SeriesSimilarity` — removed 2026-09-01

This settings block (`FullMatchDistance`/`WeakMatchDistance`,
`FullMatchBonus`/`WeakMatchBonus`, `EnableLowAmplitudePenalty` and its
amplitude-band/weight knobs, point-tolerance settings) controlled literal
row-shape template matching. It had already been audit-only since 2026-07-13
(disproven as a ranking driver — see `SCANNER_MODEL.md` "Series-template
matching"), and was deleted from `agentsettings.json`/`GetCandidatesSettings.cs`
entirely on 2026-09-01 once the audit trail itself was shown to be
uninformative: the diagnostic column it fed (`TemplateRankTier`) recorded a
real match on zero rows across its entire history in `candidates.csv`
(2306+ rows). If this section is referenced in older notes, it no longer
exists in code — check `RankingQualityScore` instead.

`FinalTopCandidates`, `SecondPassWindowMultiplier`, and
`SecondPassMinimumWindow` were removed on 2026-07-11. Ranking now runs on the
current BellUp and non-BellUp pools without a top-window cap; see the scanner
skill for the legacy score formulas used by those pools.

### `GetCandidates.TradePlan`

Do not use this to solve scanner recall/ranking issues.
Use it only after the list quality is good.

`BellUpBoostExitMinProfitPct` is the minimum target return for the BellUp
boost-body exit override. It is a decimal fraction (`0.015` = 1.5%). If the
average previous boost body produces a smaller target, the exit target is
raised to this floor; entry and stop remain unchanged. This avoids labeling a
sub-commission move as a useful BellUp win while retaining candidates whose
price may reach the more meaningful target.

Since 2026-10-05, `BellUpProfitReductionThresholdPct` (default `0.10` = 10%)
and `BellUpProfitReductionDivisor` (default `2`) reduce the final playable
BellUp profit target. Profit **equal to or above** the threshold qualifies.
The exit becomes `entry + (exit - entry) / divisor`, rounded to cents; this
divides the profit distance, not the absolute exit price. Both momentum and
boost-body exit profiles are covered, after the boost override. Entry, stop,
ranking and candidate group are unchanged. The existing
`BellUpBoostExitMinProfitPct` floor also protects reduced targets, without
raising the original target. A divisor of `1` disables reduction; values
below `1` or a negative threshold also skip it. A zero threshold applies it
to every positive BellUp target. Publication refresh rebuilds the base plan
before applying reduction once. Modified profiles get `-profit-reduced` and
the log records original/new targets and percentages. Existing saved plans
and evaluation rows are not retrospectively rewritten.

The supporting historical screen is
`docs/investigation-2026-10-05-half-profit-targets.md`. That study used a
strictly greater-than-10% condition; the implemented inclusive threshold is
the user's subsequent explicit choice.

### Experimental Win estimate (2026-10-05)

`DiagnosticsEstimatedHitRatePct` now estimates Win instead of amplitude.
`BellUpWinProbability` uses final target profit, stop distance, relative volume
and three-interval H4 RSI change. It applies only to playable BellUp with
valid prices and RSI history; other groups receive null. Ranking is unchanged.
Diagnostics include `EstimatedHitRateModel` and `EstimatedHitRateScope`.

Frozen model v1 uses 46 resolved replay plans across 28 tickers, with reduced
targets and equal total training weight per ticker. It is a ridge logistic
model (L2=4, symmetric intercept prior), with scaled/clipped inputs.
Coefficients: `docs/model-2026-10-05-bellup-win.json` and
`BellUpWinProbability.cs`. Reproduction: `tools/fit_bellup_win_probability.py`
reads the two dated analysis JSON artifacts; it does not deploy coefficients.

Scope is `WinGivenEntryAndResolution`: Open/NoEntry were excluded from fitting.
This is not unconditional ten-day P(Win) for all candidates. It is experimental,
not independently calibrated, and resolved-outcome selection introduces bias.
A wider stop can increase estimated Win without improving expected profit.
Console uses `Win~` with an explanatory legend. The estimate is assigned after
publication refresh, including singleton pools. Historical values are unchanged;
execution policy changes require revisiting labels and calibration.

## Research settings

### `Research.RecentScanDays`

Use rolling day windows for the research output retention cutoff.

`0` or a missing/negative value keeps all existing research rows and appends
new daily oracle rows after de-duplication. `1` means today's research rows
only and will drop older rows from `research_top_gainers.csv`.

### `Research.RecentEvaluationScanDays`

Limits ticker universe taken from `evaluation-dataset.csv`.

### `Research.MaxParallelTickers`

Performance lever for research generation.

## Evaluation dataset settings

### `BuildEvaluationDataset.RecentScanDays`

Limits active rebuild window, applied in `EvaluationDatasetBuilder.UpsertAsync`
(the path used by `evaluate-candidates`).

Since 2026-08-11 the cutoff is anchored to the latest `ScanTime` already
present in `evaluation-dataset.csv` before the run, not to wall-clock "now".
A gap between runs longer than `RecentScanDays` must not retroactively prune
already-saved history that was inside the window when it was last written —
the window only advances with actual run activity, as if the gap had not
happened. (Previously it used `Now()`, so a long gap between runs silently
dropped an entire block of older history in one run instead of aging it out
gradually; this cost a month of `evaluation-dataset.csv` history on
2026-08-11, recovered from a stale `Data/evaluation-dataset.xlsx` export.)

`EvaluationDatasetBuilder.RunAsync` (used by the currently-unreachable
`NormalizeEvaluationsCommand`, not wired to any CLI command in `Program.cs`)
has its own, different `RecentScanDays` filter on the raw evaluations input
and was not touched by this fix — it isn't reachable today, so it isn't an
active risk, but revisit it if that command ever gets wired up.

### `BuildEvaluationDataset.BackfillLegacyNoEntryZeroAmplitudeDays`

Temporary repair switch for legacy `NoEntry` rows with zero amplitude.
Turn it off after backfill is done.

### `CandidateEvaluation.ForwardEvaluationDays`

Controls only the forward price-history window used to evaluate each candidate.
For example, `10` means inspect up to ten days of future price history for a
candidate. It does not control how many scan dates are selected.

### `CandidateEvaluation.RecentScanDatesToEvaluate`

Controls how many most recent eligible scan dates are reevaluated. The default
is `2`; newly discovered scan dates newer than the latest date in the dataset
are also included. Keep this independent from the forward history window.

On 2026-09-29, before this setting existed, `ForwardEvaluationDays` was raised
from `1` to `10`. The old coupled behavior selected 11 trading scan dates and
2,511 candidates in the 2026-09-30 run, before open/incomplete retries. The
current configuration uses a ten-day forward window and two recent scan dates.

`ReevaluateOpenCandidates` is separate: when enabled, previously saved open
rows older than the selected date window are added for another evaluation,
while their scan date is still within `ForwardEvaluationDays`. Open rows whose
forward window has elapsed are not retried.

### `CandidateEvaluation.AllowedOutcomesToEvaluate`

An array of saved evaluation outcomes eligible for reevaluation. Candidates
with no saved evaluation are evaluated once so new scans enter the report.
Existing rows are reevaluated only when their latest saved outcome is in this
list. The current value is `["Open"]`; add another quoted outcome as a
comma-separated array item to include it. The same filter applies to incomplete
metric retries. Open rows are retried only while their forward window remains
active.
