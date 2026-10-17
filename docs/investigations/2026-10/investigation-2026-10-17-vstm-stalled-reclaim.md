# VSTM: recognized Daily hook rejected as Stalled

Recorded 2026-10-17. Local candidate/evaluation/log/cache investigation of
scan 2026-10-15 06:43:02 America/New_York. No production rule, model, settings
or saved outcomes changed. Application, broker commands, builds and tests
were not run; only read-only Python inspection/reconstruction was executed.

## Saved decision

Source: Data/logs/log-20261015_0536.log, candidates.csv and
evaluation-dataset.csv joined by Ticker + ScanTime.

VSTM was recognized on D1, bend bar October 7, five completed D1 bars old.
It was considered from both a saved loser watch and live TOP_PERC_GAIN.
At publication refresh October 15 06:35:48 the fresh M5 reference was 8.11,
Daily mid 7.9165, last completed D1 October 14 Close 7.71. H4 had no recognized
seed. The saved reason is Phase=Stalled, Recovering=False and
NotReady=EpisodeRecognized=True, Phase=Stalled. Candidate publication at
06:43:03 retained Other and ReversalRejected with zero entry/exit/stop.
This is an admission rejection, not a real non-fill of an active saved plan.
The evaluation preserves the recognition label; Match is not trade admission.

## Exact phase constraint

ReversalHookPhaseClassifier.Analyze preserves the episode as recovering only
when completed close exceeds the pre-bend close, the last completed MACD
histogram is >= the bend histogram, and price is above its recent minimum.
Live reclaim above mid can resume recovery, but retains the same completed
histogram >= bend histogram requirement.

Cache reconstruction with Decimal and the logged 167-bar D1 depth agrees:

- Pre-bend October 6 Close 7.75; bend October 7 Close 8.27.
- Bend histogram approximately -0.022980832.
- October 14 Close 7.71; histogram approximately -0.062339423.
- Fresh October 15 8.11 exceeds both current mid 7.9165 and pre-bend 7.75.
- The closed histogram remains worse than the bend value. Thus price reclaim
  cannot revive the episode under the implemented rule; no new D1 histogram
  exists until the next Daily bar completes.

RefreshReversalForPublication rejects Stalled before checking remaining
BellUp precedence/support and rebuilding the fresh plan. This identifies the
observed immediate veto; bypassing it alone has not been replayed through
all remaining application checks.

## Preliminary plan and available subsequent path

The selected TOP_PERC_GAIN context built a preliminary plan at 06:27:00:
Entry 8.05, Stop 7.57, StopLimit 7.55, Exit 8.69. The earlier loser context
had a different preliminary 7.95/7.56/8.35 plan. Neither remained executable
in the published row: the rejection zeroed its trade levels.

For the selected preliminary levels, cached M5 after publication shows:

- First entry touch: 08:50, O8.04/H8.06/L8.03/C8.06, Volume1200.
- First target-or-stop touch: target 8.69 at 12:20.
- Lowest Low from entry through that target: 7.77, above stop 7.57.
- Target return relative to entry: (8.69/8.05-1)*100 = 7.95%.
- All 43 consecutive M5 starts from 08:50 through 12:20 are present.
  Neither target nor stop is in the first entry M5, so this check does not
  presume their order within that entry bar.

This is a bounded counterfactual touch check of an already logged preliminary
plan, not an application evaluation, broker fill, full fresh-plan replay or
independent validation of a new readiness rule. The retrospective cache does
not prove timely data availability at the original decision.

## Amplitude interpretation

Saved evaluation: AmplitudePct25.23, ScanPrice8.11, minimum7.77 at09:45,
maximum9.73 at13:50, MinFirst. Positive potential from ScanPrice is19.98%.
25.23% is the low-to-high excursion (9.73/7.77-1), not profit from the saved
scanner reference or the zeroed plan. The preliminary-plan check is the more
direct evidence of a potentially missed played winner under current objectives.

## Decision

This is a concrete missed recovery candidate under the closed-Daily-momentum
readiness rule. It motivates testing whether a fresh price reclaim can revive
an established hook with declining completed-D1 momentum. It does not justify
removing MACD checks globally, promoting every Stalled episode, or claiming
that a modified current scanner would necessarily admit and win this plan.
Compare successful reclaims with failed rebounds on saved scan-time inputs,
matching plan policy/horizon, before changing the veto. Keep this case as a
regression/research example rather than a threshold-tuning target.
