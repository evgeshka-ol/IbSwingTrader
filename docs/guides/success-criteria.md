# Current success criteria

Confirmed by the user on 2026-10-17. This is the canonical project objective
for scanner, evaluation and research work. It supersedes the former
amplitude-first / capture-more-than-10% objective in older documents.
This clarification changes documentation, not application behavior or models.

## Minimum goal

The user's minimum goal is «хотя бы один гарантированный победитель на 1-м
месте в списке, даже пусть и с минимальным профитом в 1.5%»: a dependable
winner at rank 1, with profit of at least 1.5% sufficient for the minimum goal.
This is the required target, not an achieved guarantee or a calibrated 100%
probability. Assess it on subsequent scan results; do not claim it has been
achieved from an example, an amplitude spike or a high confidence estimate.

Inspect the original first playable row of each scan before aggregate results.
Record CandidateGroup and the ranking scope explicitly: current output has
separate BellUp and ReversalHook groups. Do not silently invent a combined
ranking, restrict the objective to BellUp, or replace a losing/non-entered
rank-1 row with a later winner. Other/zero-plan rows are diagnostics.
A rank-1 NoEntry or unresolved Open does not establish the minimum goal.

## Priorities, in order

1. Number of winners among candidates that actually entered the game.
   In evaluation data, participation means an entry under the saved plan
   (EntryTime), not merely publication or pattern admission. Report Win,
   Loss and entered-but-Open counts, the entered denominator, and the
   corresponding Win share. These are evaluated trades, not broker fills.
2. Profit percentages of those winners. Report entry-relative profit from
   the saved entry and achieved evaluated exit; an unhit PlannedProfitPct
   or future maximum is not earned profit. A 1.5% winner can satisfy the
   minimum goal; amplitude below 10% is not automatically a failure.
   Keep losses visible alongside winning profits. Distinguish report/plan
   percentages from net broker returns including fees and slippage.
3. Agreement between the scanner's saved DiagnosticsEstimatedHitRatePct
   and actual results: does a stated confidence correspond to observed
   Win frequencies, and do higher estimates separate winners from losers?

## Denominators and unfinished outcomes

Report entered Win / Loss / Open and published NoEntry separately. Do not
count NoEntry as a played loss or Open as a resolved loss. Win/entered and
Win/(Win+Loss) answer different questions; show their denominators and
observation horizon. Open can be pending/censored; inspect window coverage
rather than treating every Open as a completed result. Empty populations
have no measurable win rate. Repeated ticker scans are separate plans but
not independent confirmations; retain date/ticker grouping in validation.

For confidence calibration, retain the estimate saved at publication and
join candidates to outcomes by ticker and ScanTime. Match model/scope and
entry/exit/stop policy. The current experimental BellUp model
bellup-win-ridge-2026-10-05-v1 estimates WinGivenEntryAndResolution, so its
compatible observed frequency is Win/(Win+Loss). Report entered Open and
NoEntry alongside it to expose selection and incomplete horizons. Do not
present this conditional rate as unconditional success for every publication.
Historical amplitude estimates and unavailable estimates form separate cohorts.
Use probability buckets with sample sizes and calibration error (for example
Brier score) alongside discrimination; AUC alone is not calibration.

## Supporting diagnostics and validation

AmplitudePct, next-day research coverage, shape, freshness and recall help
explain successes and failures. They do not replace the three priorities.
A large later move does not rescue a played Loss or a missing rank-1 Win.
Keep recognition, ranking and execution mechanics distinguishable to locate
a defect, while judging the published candidate plus its plan by the result.
There is no blanket rule to postpone plan work until amplitude is high.

Before changing admission/ranking features, thresholds or confidence models,
validate against the declared Win/profit target using saved scan-time inputs
and subsequent outcomes, then later dates with adequate horizon coverage.
Historical amplitude AUC remains evidence for that older target, not proof
of present Win quality. No future values may enter scanner features.
