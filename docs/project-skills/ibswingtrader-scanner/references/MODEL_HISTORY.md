# Scanner model history

Historical family-era rules and design proposals extracted from the scanner
skill on 2026-10-17. Read for old datasets, removed template matching and
unvalidated proposals. They do not define current admission or success.
Current objectives: [success criteria](../../../guides/success-criteria.md).
Current recognition/readiness: [Series Playbook](SERIES_PLAYBOOK.md).
The old below-Daily-mid split and amplitude thresholds below are historical.

## Legacy family model (historical context)

The following `Runaway`/`Reversal` family split describes the former model
and legacy fields. It is retained to explain historical datasets and older
analysis notes; it does not define current candidate output groups.

- `Reversal`: below-mid / pullback / return-to-mean style ideas.
- `Runaway`: above-mid / runaway / continuation style ideas.

Internal `Runaway` subtypes:

- `BellUp`: squeeze-to-launch continuation, the canonical direct form
- `Runaway`: strict kink/acceleration launch
- `LaunchContinuation`: real Bollinger launch with strong daily/H4 expansion
- `PullbackContinuation`: constructive continuation after a pullback

The former final `Runaway` admission was intentionally strict: the candidate
had to confirm `BellUp` on real Bollinger rows in either `H4` or `Daily`. (The
literal-template match/veto mentioned in older notes was removed 2026-09-01 —
see "Series-template direction" below.) Other above-mid continuation subtypes
remain diagnostic only.

Do not mix the two mentally or in code. They are opposite regimes and need different ranking logic.

Hard classification rule:

- The latest completed daily Bollinger mid bar is the primary hard split.
- Infer the expected latest closed D1 date from the dominant latest date in
  the current market-wide D1 data. Do not assume that the previous weekday was
  a trading day; exchange holidays must not make every family split unknown.
- If the ticker was below the daily Bollinger mid on the last closed daily bar, it belongs to `Reversal`.
- If the ticker was at or above the daily Bollinger mid on the last closed daily bar, it belongs to `Runaway`.
- For a recent IPO without enough reliable D1 rows, use H4 close versus H4 mid
  as a scoped family fallback and detect `ReversalHook` on H4 rows. Do not use
  this fallback when sufficient D1 history exists.
- Weekly and H4 context only refine subtyping, promotion, and ranking inside the family.
- Do not retune this family split when trying to improve list quality; keep the category boundary fixed and work only on promotion, ranking, and trade-plan behavior after the split.

Both families should produce meaningful future amplitude:

- `Runaway` should be the strongest next-day research proxy.
- `Reversal` should still usually produce `AmplitudePct > 10%`.
- If `Reversal` amplitude is below 10%, treat that as scanner failure, not a trade-plan issue.

Current final `ReversalHook` promotion uses the ReversalHook pattern without
the former hard below-mid split. See the current output contract above and
`SERIES_PLAYBOOK.md` for current readiness rules.
Weekly rows remain context only. Full row-shape checklist (incl. the
freshness requirement on the lower-band turn) and worked examples:
`SERIES_PLAYBOOK.md` "ReversalHook" — do not restate or fork that checklist
here.

**2026-09-01 finding: this gate carries no measurable signal.** Every one of
`IsReversalHookPattern`'s 14 boolean sub-flags, tested individually against
194-260 decided Win/Loss `DiagnosticRejected` Reversal rows in
`evaluation-dataset.csv`, came back AUC 0.43-0.54 (noise); the compound gate's
pass/fail count has no monotonic relationship with outcome either.
`CalculateReversalHookQualityScore` itself: AUC 0.51. See
`REVERSAL_EDGE.md` "Open items" for the full result and the
continuous-feature sweep that followed it. Per the user's explicit choice,
this is not being fixed by retuning the gate/score (nothing here has been
shown to carry signal to retune) — instead raw Daily/H4 OHLC candle series
are meant to be captured (`RecentDailyOpenSeries/HighSeries/LowSeries`,
`RecentH4OpenSeries/HighSeries/LowSeries/CloseSeries`, added 2026-09-01) so
the candle-level mechanics described in `REVERSAL_EDGE.md`'s `NAT` example
can eventually be tested directly. **Correction (2026-09-03): this capture is
not actually happening in `evaluation-dataset.csv` in practice** — checked
across all 2925 rows, these OHLC fields are non-empty in only ~4.8% (140
rows), all sharing one single `ScanTime` batch, not the normal per-scan flow;
everywhere else only the derived Bollinger/MACD/RSI series are present. Do
not assume raw candles are available from this CSV — reconstruct them from
`Data/cache/<TICKER>.json` instead (see `REVERSAL_EDGE.md` and
`tools/Backfill-ReversalCandles.ps1`) until the write-path gap is actually
fixed.

**2026-09-03 architecture decision:** the binary `IsReversalHookPattern` gate
is being replaced, not retuned — see `REVERSAL_EDGE.md` "Open
items" for the full reasoning, the `NAT` case that motivated it (a real
intraday capitulation-drop entry the daily-mid gate structurally cannot see),
and how this differs from the already-disproven July series-similarity
approach. Two pieces, both still unimplemented/unvalidated:

1. A layered timeframe funnel, in this exact role order: **Weekly** = macro
   risk gate only ("safe to hold overnight" — not a pattern check). **Daily**
   = confirm or defer the setup ("play it" vs "not today"). **H4** = same-day
   vs today-into-tomorrow timing. **M15** = pure execution (entry/exit/stop
   numbers), never a go/no-go layer.
2. Admission scored as **graded similarity to a small, hand-curated exemplar
   chart library**, validated by AUC against `evaluation-dataset.csv` before
   being wired in — not a binary threshold/gate.

Apply the same rigor as everywhere else in this file: nothing here gets
wired into admission or ranking until it clears an AUC/holdout/asymmetry bar.

`Reversal` is the user's own years-proven manual trading edge (this app's job
is to remove emotion and scale past the broker scan API limit, not to
discover the pattern) and should get more attention than `Runaway` tuning
when both need work — see `REVERSAL_EDGE.md` for why, the precise
"mid band bends like a hypotenuse" trigger definition, the signal hierarchy,
a full worked example, and open items that were investigated but
deliberately left unimplemented pending more evaluation data.

## Series-template direction — removed 2026-09-01

Literal series-similarity template matching (`ApplySeriesSimilarityDiagnostics`,
`CalculateSeriesSimilarityMatch`, `LoadSeriesSimilarityTemplatesAsync`,
`ResolveTemplateRankTier`, the `SeriesTemplateRankTier` enum, and the
`Diagnostics.SeriesSimilarityTemplateTicker/Family/Bonus`,
`LowAmplitudeTemplateTicker/Penalty`, `TemplateRankTier` columns in
`candidates.csv`) has been **deleted from the codebase**, not merely demoted.
It had already been reduced to an audit-only side-channel on 2026-07-13 (see
`SCANNER_MODEL.md` "Ranking implementation status") after being disproven as a
ranking driver; this session found the audit data itself was never
informative either — across the entire history of `candidates.csv` since that
diagnostic column started being written (2306+ rows), `TemplateRankTier` was
`"None"` in 100% of them, meaning the low-amplitude admission veto that used
to sit inside `IsTodayResearchLikeCandidate` could structurally never have
fired. Both were removed together. `SeriesTemplateFamily` the enum still
exists and is still used, but purely as a plain discriminator to pick
`CalculateRunawayLaunchQualityScore` vs `CalculateReversalHookQualityScore` —
it no longer drives any template matching. `SERIES_PLAYBOOK.md`'s old
"Template sources"/"Practical use" sections have been replaced with a pointer
to this history; do not resurrect that methodology without new evidence.


## Migration record and BBNX exemplar

The following is the September 23 migration record. Its IPO-only H4 route
was superseded by independent D1/H4 episode recognition on October 6.
The proposed full episode-transition history remains distinct from the
implemented current phase diagnoses. See SERIES_PLAYBOOK.md for live rules.

## Pattern-first groups (implemented 2026-09-23)

The former `Runaway`/`Reversal` split was too dependent on the latest Daily
Bollinger mid. BBNX demonstrated the failure mode: a ReversalHook can begin
below the mid, cross above it, and then produce a large continuation. The
pattern-first groups were implemented in commit `d4a4f1d`:

- `BellUp`: a confirmed BellUp pattern on H4 or Daily;
- `ReversalHook`: a confirmed Daily ReversalHook, with H4 as a fallback for
  recent IPOs that lack reliable Daily history;
- `Other`: neither pattern confirmed or a confirmed pattern rejected as not
  currently trade-ready.

The live console and CSV use these groups. Candidate processing uses one pool
until pattern classification is complete; the former Daily-mid split no
longer defines the output category. BellUp is checked before ReversalHook. A
detected BellUp that fails timing or readiness remains `Other`, rather than
being relabeled ReversalHook. ReversalHook playability also requires
higher-timeframe support.
The ReversalHook implementation was subsequently strengthened to check
support from the next higher available scale; the check can accept either a
higher-scale BellUp or an early return-to-mid context. A Daily hook checks
Weekly context, while the H4 IPO fallback checks Daily context.

Pattern episodes should eventually retain timeframe and phase metadata (`Active`,
`Exhausted`, or `Transition`) and, where available, the previous pattern. A
useful transition shape is `ReversalHook -> BellUp -> Exhausted`; BBNX is the
first exemplar for this state model: Daily ReversalHook around Aug 3-7, a
short H4 ReversalHook around Aug 3-4, H4 BellUp beginning Aug 7 and exhausting
after the Aug 11 impulse, plus a later Daily ReversalHook beginning Sep 14.
This episode-history model is a future direction; current output stores the
detected group and confirmation reason, not a complete transition history.

BBNX remains an exemplar, not a hard-coded template. The group migration is
live; further ReversalHook detection or ranking changes still need more
cache-confirmed examples and empirical validation against realized outcomes.

