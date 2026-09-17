# Candidate CSV review fields

Updated 2026-09-17.

`TotalScore` replaces the flattened `ScoreScore` header. It is the sum of
DailyScore, WeeklyScore and EntryScore, not a percentage probability.
Console `conf=...%` comes from `DiagnosticsEstimatedHitRatePct`, which remains
a separate column. Internal model names and score calculations are unchanged.

Within each scan's `Other` section, rows whose `PatternVerdictReason` starts
with `BellUp confirmed on ` appear first. Existing rank, profit, score and
ticker tie-breakers remain unchanged within the two subsets. DisplayRank
follows this order. Scan chronology and Runaway/Reversal ordering are unchanged.

Newly rejected BellUp rows append a brief reason from the rejecting gate,
for example `BellUp confirmed on H4; Recent boost`. Labels include `Not ready`,
`Late phase`, `Terminal pullback`, `Exhausted`, `Lower-band turn`,
`Missing candles`, `Missing data`, `Structure broken`, `Reversal unconfirmed`
and `H4 contradiction`. This distinguishes pattern recognition from trading
eligibility without changing admission rules. Full diagnostics remain in logs
and ContextNotes. Historical rows do not gain reconstructed rejection reasons.

The changes apply when candidate output is next written; evaluation verdict
generation is unchanged. Regression checks live in `tests/CandidateCsvChecks`
and `tests/BellUpBoostExitChecks`; the user runs builds and tests.
