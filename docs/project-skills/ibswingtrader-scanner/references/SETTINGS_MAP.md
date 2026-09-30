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
