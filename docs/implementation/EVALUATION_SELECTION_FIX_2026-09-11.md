# Evaluation selection fix, September 11

The 04:49 evaluation run selected September 10 and 11, but processed the
fresh September 11 04:39 scan first. The ten-minute safety lag left less
than a minute of post-scan history. After ten consecutive data failures it
aborted at 17/279 results; 14/17 failures exceeded the merge guard, so the
dataset was not updated. September 10's 133 candidates remain in the CSV.

Changes in EvaluateCandidatesCommand:

- Current/future exchange-day scans are deferred until a later exchange
  date. This applies even to an evening evaluation on the same date.
- The safety-lag timestamp cutoff is enforced per candidate, not only while
  selecting dates. The same policy applies to Open and incomplete retries.
- Selected historical dates are processed chronologically first, followed
  by older retries. File order no longer gives a new scan priority.
- Final logging separates processed count from persistence status:
  `DatasetMerge=Completed` or `DatasetMerge=NotPerformed`. Merge failure
  guards remain enabled; no fabricated results are saved.

The date policy uses exchange-local timestamps and captures the current
time once for selection. Neither candidates nor evaluation data was edited.

Verification for the user:

1. Build the application.
2. Run `dotnet run --project tests/EvaluationSelectionChecks` for eight
   isolated eligibility checks (no broker or application dependency).
3. Run the normal evaluator. With the September 11 morning inputs it should
   report 133 current-day candidates deferred and select September 9 and 10
   under ForwardEvaluationDays=1. No September 11 row should enter retries.
4. Confirm `Evaluation step: evaluation dataset merge completed` and
   `DatasetMerge=Completed`, then inspect ScanTime September 10 in the CSV.

Builds/tests/application runs were not executed by Codex, per the project
responsibility boundary. Static diff checks passed. Broker data availability
can still independently prevent evaluation or persistence.
