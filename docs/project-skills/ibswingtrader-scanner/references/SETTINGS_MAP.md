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

- **Current output categories (2026-09-09):** the user's intended categories
  are `Runaway` (BellUp only), `Reversal`, and `Other` (matches neither
  playable category). Preserve rejected rows for analysis without treating
  `Other` as a playable Runaway recommendation.
  - Verified current implementation: `CandidateResultWriter.WriteAsync`
    separates `CandidateSource=Other` from the same-day pool into an `Other`
    console section and excludes it from the Runaway summary. The finder
    stores Runaway rejects this way with a price snapshot and zero plan.
  - CSV compatibility caveat: the 2026-09-08 `candidates.csv` still labels
    these rows `CandidateGroup=Runaway`, whereas evaluation labels them
    `Other`. Join by ticker and scan timestamp, and inspect source as well
    as group before computing playable Runaway top-1 results. A rank-1
    `Other` row in that CSV is not the console's first playable Runaway.
  - Reversal rejects are still present as `DiagnosticRejected` in the
    Reversal pool. The desired "neither pattern -> Other" rule is not yet
    applied uniformly to both families in the inspected implementation.

- `EmitAllSeenCandidates`: **deliberately `true`, not a bug.** Historical
  behavior before the `Other` separation described above: when `true`,
  `CandidateResultWriter` writes and displays every seen candidate — including
  ones the admission gate rejected (`CandidateSource=DiagnosticRejected`,
  `BellUp`/`ReversalHook` not confirmed) — ranked together with genuinely
  admitted ones (`Primary`/`SameDayContinuation`) by the same quality score, in
  the same console output and `candidates.csv` sections, with no source marker
  visible in the console.
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
  - Analysis on 2026-09-01 against `evaluation-dataset.csv` (23 scans): current
    rank-1 already hits `AmplitudePct >= 10%` in at least one of
    Runaway/Reversal on 20/23 scans (87%) — Runaway rank-1 alone 74%, Reversal
    rank-1 alone 52%, both categories together only 9/23 (39%). The properly
    *admitted*-only pool performs worse in this sample (Runaway admitted-only
    top pick 52% vs 71% for the rejected-but-top-scored pool; Reversal
    `Primary` admissions exist in only 6/23 scans and never cleared 12%
    amplitude) — another reason not to re-enable the gate as a shortcut fix
    right now.

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
`SecondPassMinimumWindow` were removed on 2026-07-11: `Reversal` reranking
previously capped its second-pass window using these, silently leaving part of
a large list unadjusted by template matching. Both `Runaway` and `Reversal`
now always rerank their full candidate pool.

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
