# BellUp late-entry ranking penalty

Applied as a general Runaway quality-score penalty for the user's SWKS,
QRVO and FRO examples. It is not an admission veto. The confirmed
timeframe is not persisted in CandidateDetails, so the ranking calculation
checks both saved H4 and Daily OHLC series and uses the stronger penalty;
the actual BellUp admission/entry timing still uses the confirmed timeframe.

The latest saved candle must be green, close in the upper 30% of its range,
and have a body comparable to at least one previous boost. A boost is the
existing literal rule: positive body at least twice the absolute body of
the immediately preceding candle. Search backward from the latest candle;
one boost is enough for a soft penalty, two boosts produce the stronger
penalty. Current-body ratio must be between 0.35x and 2.50x the reference
boost body, avoiding unrelated tiny candles and isolated spikes.

The quality score subtracts 0.75 points for one prior boost or 1.50 points
for two. With the existing adjusted-rank multiplier of 50, these correspond
to 37.5 or 75 rank points. Entry, stop and exit are unchanged. The penalty
does not claim that a later continuation is impossible; it only expresses
that the scan entered near a comparable prior impulse's high and therefore
deserves lower priority. The two-boost exit target remains a separate rule.

One prior boost deliberately remains actionable as a fallback, per the
user's instruction. If no qualifying prior boost exists, or the current
candle is not near its high, no penalty is applied. Existing lower-band-turn
and boost timing vetoes remain separate and can still move a candidate to
Other.

Added to `BellUpLateEntryPenalty.cs` and wired into
`CalculateRunawayLaunchQualityScore`. Tests cover two boosts, one boost and
non-peak candles. Run them after rebuilding:

    dotnet run --project tests/BellUpBoostExitChecks

Historical CSV rows and evaluation results were not rewritten. This rule
will be visible only in future `DiagnosticsRankingQualityScore` and display
rank values. Build/tests/scanner were not run by Codex.
