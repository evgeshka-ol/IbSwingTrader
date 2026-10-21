# SMCI: after-hours H4 hook rejected by Daily support

Recorded 2026-10-21. Sources: candidates.csv, evaluation-dataset.csv,
Data/cache/SMCI.json and logs log-20261021_0300.log/log-20261021_0424.log.
Times below are America/New_York. No application, broker, build or test run
was performed by Codex.

## Three saved decisions

- October20 04:19:40: Other, ScanPrice31.53, H4 recognized/Stalled,
  seed October19 08:00. The fresh quote was below the pre-bend close32.78;
  improving provisional H4 alone does not invalidate this price failure.
  The narrow RCUS fresh-early-recovery correction does not admit this row.
- October20 17:30:22: Other, ScanPrice36.59, recognized/MidCrossed.
  Rejection: D1 has no BellUp or early ReversalHook support, mid37.99,
  room3.83%, mid slope-3.23%, histogram-1.42.
- October21 04:24:09: Other, ScanPrice36.71, recognized/MidCrossed.
  The same D1 support gate rejected it: room3.49%, slope-3.23%,
  histogram-1.42. The completed H4 seed was now October20 16:00.

The early-D1 support branch requires room>=2%, mid slope>=-0.20% and
histogram both improving and nonnegative. It can veto an already recognized
active H4 reversal while the slower Daily picture remains weak. This is
distinct from the RCUS phase defect. No Daily-support threshold or bypass
has been changed on the strength of this example.

## Impulse timing

Cached October20 16:00 H4: O32.47/H37.66/L32.37/C37.55. Thus the main
impulse occurred after the October20 regular close, not for the first time
in October21 premarket. The user confirmed this chronology. The evening
scan saw the move in progress; the next morning was continuation.

## Evaluation coverage limitation

The morning report row saved Max37.66 at19:55 and Min31.54 at04:20,
old range amplitude19.40%. From ScanPrice31.53 to Max37.66 is19.44%.
The inspected M5 cache has only52 October20 bars: six in04:xx and46
in16:xx–19:xx, with no daytime bars. Native H4 instead records a
daytime minimum30.92 in the08:00 bar. Therefore the reported minimum
does not describe the complete post-scan path, and cannot establish whether
the initial move was up or down. Replacing the denominator alone does not
repair missing coverage. No historical report/cache values were rewritten.

The evening row's saved extrema were Max37.66 at19:55 and Min36.36
at October21 04:10; its old negative range was-3.57%. The new max-first
ScanPrice denominator36.59 would give-2.92%, retaining the order marker.
These are diagnostic extrema, not entered-plan outcomes; all saved plans
were zero and the evaluated rows were NoEntry.
