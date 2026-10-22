# ReversalHook origin: post-BellUp pullback versus prolonged deep decline

Recorded 2026-10-22 after the user's chart review of October21 evening
candidates. This is a proposed taxonomy with measured supporting evidence,
not a deployed automatic classifier or admission/ranking change.

## User's distinction

- GRAL/DDOG/OKTA: a return toward mid after a late pullback below mid in an
  exhausted BellUp. They pass the broad below-mid/reclaim definition but are
  not the user's canonical prolonged-decline reversal.
- CLSK/IREN/CIFR/AESI/FMC: candidates for the canonical reversal after a
  substantial decline and prolonged stay below mid. A local H4 recovery
  does not establish a completed Daily reversal; CLSK could instead develop
  into a Triangle/consolidation.

Keep BellUp/ReversalHook/Other groups unchanged. Proposed origin labels:
PostBellUpRecovery, DeepDeclineRecovery, Unclassified. These describe the
origin, independently of signal frame and phase. A DeepDeclineRecovery
candidate with H4 Active/D1 None must not be relabeled confirmed Daily hook.
Ambiguous or insufficient history must remain Unclassified.

The existing W1 BellUp support string is an algorithmic envelope verdict.
It does not mean Weekly price is below mid or that a Weekly mean-return
figure is drawing price upward. User chart review identifies an after-growth
context in GRAL/DDOG/OKTA despite that saved support string. Do not use it
as a substitute for measuring origin on the candidate's Daily history.

## Measured Daily origin context

Read-only Python reconstruction using tools/analyze_h4_to_daily_reversal.py
native_history on full cached native D1 prefixes, cut off after the closed
October21 bar. EMA20 center, SMA20 seed, population sigma20. No application,
broker, build or test command was run. These are current-cache retrospective
measurements, not evidence that every bar was delivered on time yesterday.

For a currently below-mid close, count its consecutive completed D1 run.
For a first close back above mid, count the immediately preceding below-mid
run. Depth is the maximum close discount to its own contemporaneous EMA
within that run, not scan-to-future-low drawdown or future amplitude.

| Ticker | Consecutive D1 closes below mid | Maximum below-mid depth | User-described origin |
|---|---:|---:|---|
| GRAL | 3 before reclaim | 6.63% | Post-BellUp pullback |
| DDOG | 4 before reclaim | 6.90% | Post-BellUp pullback |
| OKTA | 4 before reclaim | 9.32% | Post-BellUp pullback |
| CLSK | 18, ongoing | 25.64% | Prolonged deep decline |
| IREN | 19, ongoing | 27.27% | Prolonged deep decline |
| CIFR | 18, ongoing | 34.75% | Prolonged deep decline |
| AESI | 11, ongoing | 17.86% | Prolonged deep decline |
| FMC | 29, ongoing | 36.55% | Prolonged deep decline |

The groups separate strongly in these eight examples, but that is not an
independent validation or justification for fitting thresholds to them.
Short duration alone does not prove a preceding BellUp; historical curve
evidence must establish that label, otherwise leave origin Unclassified.

## Pending definition before automatic classification

The user has been asked whether prolonged means at least10 closed D1 bars,
at least5, or whether to export duration/depth first without a hard category
threshold. Suggested meaningful depth is the already agreed5%, but applying
that wishlist enrollment threshold to subtype semantics also needs clarity.
No threshold has been adopted and no automatic subtype column has yet been
added. New subtype metadata should be saved from the scan-time snapshot and
preserved into evaluation; old rows must not be relabeled using later bars.

Origin labels should initially describe candidates rather than change their
admission, rank, stops/targets or confidence model. Assess later entered
outcomes by origin before deciding whether different policies are warranted.
